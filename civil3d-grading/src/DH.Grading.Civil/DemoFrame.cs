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
    internal const double W = 430, H = 348;   // ★[v117.18] «도움말» 머리 줄은 틀 밖(체크로 틀을 접어도 머리 줄은 남음) → 틀은 그림 + 설명 두 줄만

    /// <summary>★[v117.18 · JACK 1116 «도움말 글머리 = 체크박스 · 끄면 애니메이션 창이 접힘 · 프로그램에 기억»] 창별 값 HKCU\Software\DHGrading\HelpShow_{key}(DWord · 없으면 보임).</summary>
    internal static bool IsShown(string key)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(GradingSettings.PrefsRegKey);
            return !(k?.GetValue("HelpShow_" + key) is int v) || v != 0;
        }
        catch { return true; }
    }

    /// <summary>값이 바뀔 때만 쓴다 · 화면 없는 실행에서는 안 씀.</summary>
    internal static void SetShown(string key, bool on)
    {
        if (SelfCheckLog.Headless || IsShown(key) == on) return;
        GradingSettings.SaveUserPrefInt("HelpShow_" + key, on ? 1 : 0);
    }

    /// <summary>★[v117.15.1 · JACK 1112 «모든 애니메이션 창 좌측 상단에 도움말이라는 글머리 — 도화 스타일로 박스도»] 도화 청록 바탕 · 흰 굵은 글 · 둥근 상자 + ★[v117.18] 체크 칸(켜짐 = 흰 칸에 청록 ✓).</summary>
    static Border HelpBadge(bool on, out Action<bool> setOn)
    {
        var tick = new TextBlock { Text = "✓", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
        var box = new Border { Width = 14, Height = 14, Background = Brushes.White, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = tick };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = "도움말", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        setOn = v => tick.Visibility = v ? Visibility.Visible : Visibility.Hidden;
        setOn(on);
        return new Border
        {
            Background = DhBrand.Brand, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left, Height = 22, Cursor = System.Windows.Input.Cursors.Hand, Child = row,
            ToolTip = "끄면 설명 칸 접힘",
        };
    }

    /// <summary>설명 칸들을 한 틀에 담는다 — 칸마다 가로 · 세로 꽉 채움 · 맨 위에 «도움말» 체크 머리 줄(<paramref name="key"/> = 창 이름 · 레지스트리 HelpShow_key).</summary>
    internal static FrameworkElement Wrap(string key, params FrameworkElement[] hosts)
    {
        var g = new Grid { Width = W, Height = H };
        foreach (var h in hosts)
        {
            h.HorizontalAlignment = HorizontalAlignment.Stretch;
            h.VerticalAlignment = VerticalAlignment.Stretch;
            h.Margin = new Thickness(0);
            g.Children.Add(h);
        }
        var vb = new Viewbox { Child = g, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Margin = new Thickness(0, 0, 0, 8) };
        bool on = IsShown(key);
        var badge = HelpBadge(on, out var setOn);
        vb.Visibility = on ? Visibility.Visible : Visibility.Collapsed;   // 접힌 틀은 안 보임 → 안의 타이머도 멈춤(FixDemoView.IsVisibleChanged)
        badge.MouseLeftButtonUp += (_, e) =>
        {
            on = !on;
            setOn(on);
            vb.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            SetShown(key, on);
            e.Handled = true;
        };
        var st = new StackPanel { MaxWidth = W * 1.25 };
        st.Children.Add(badge);
        st.Children.Add(vb);
        return st;
    }
}
