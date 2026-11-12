using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★[v117.12 · JACK 1112 «측구 설정은 별도 팝업으로 — 옹벽 단면 표처럼»] <b>측구 단면</b> 창 — 형식 · 치수 · 그레이팅 · 경고.
/// <para>칸 · 처리기는 진입도로 도킹창(<see cref="RoadPalette"/>)이 그대로 갖고 있다 — 이 창은 그 칸 묶음(<c>body</c>)을 잠깐 담는 틀일 뿐이다(저장 · 켜짐 규칙 그대로).
/// 닫으면 칸 묶음을 비워 다음에 다시 쓴다(한 창에만 붙을 수 있어서). 띄운 채로 도면을 만질 수 있다(모양 없는 창).</para></summary>
internal sealed class RoadDitchWindow : Window
{
    readonly Border _host;

    internal RoadDitchWindow(UIElement body, System.Action onClose)
    {
        Title = "DH 측구 단면";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        MaxHeight = SystemParameters.WorkArea.Height;

        _host = new Border { Padding = new Thickness(14, 10, 14, 6), Child = body };
        var (_, close) = DhBrand.FooterButtons("닫기");
        close.Content = "닫기";
        close.Click += (_, __) => Close();   // 모양 없는 창은 IsCancel만으로 안 닫힘
        DhBrand.Dress(this, "측구 단면", "형식 · 치수(mm) · 그레이팅", _host, close);
        Closing += (_, __) => { try { System.Windows.Input.Keyboard.ClearFocus(); onClose(); } catch { } };
        Closed += (_, __) => { try { _host.Child = null; } catch { } };
    }
}
