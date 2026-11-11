using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// [모델에 넣기] 팝업(JACK 1111 인터뷰 «둘 다 하되 고를 수 있게 · 3D 선은 팝업에서 넣을지 고름 · 다시 넣으면 지난 것 바꿈»).
/// 고른 것은 다음에 열 때 그대로(PC 기록). 설명 글은 두지 않음 — 이름표 · 숫자 · 풍선 글만.
/// </summary>
internal sealed class InsertDialog : Window
{
    public InsertOptions? Options { get; private set; }

    public InsertDialog(UiTheme ui, Region3D region, int madeCount)
    {
        Title = "모델에 넣기";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ui["Bg"];
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var (exact0, topo0, lines0) = Log.InsertChoice;
        bool hasMesh = region.Meshes.Count > 0, hasLines = region.Lines.Count > 0;
        var exact = ui.Check($"정확한 면 (DirectShape) · 세모 {region.Triangles:N0}", hasMesh && exact0,
            "Civil 세모 그대로 — 옹벽 수직 단차까지 Civil과 같음. Revit 지형처럼 두께 · 편집은 안 됨.", "insExact");
        var topo = ui.Check("지형 솔리드 (Toposolid)", hasMesh && topo0,
            "Revit 지형 — 두께 · 재료 · 단면 자르기 됨. Revit이 점으로 삼각망을 다시 만들어 옹벽 단차 같은 곳은 Civil과 달라질 수 있음.", "insTopo");
        var lines = ui.Check($"3D 선 · {region.Lines.Count:N0}", hasLines && lines0, "옹벽 · 소단 · 사면 · 경계 3D 선(DirectShape 선)", "insLines");
        exact.IsEnabled = topo.IsEnabled = hasMesh;
        lines.IsEnabled = hasLines;
        foreach (var c in new[] { exact, topo, lines }) c.Margin = new Thickness(0, 0, 0, 8);

        var ok = ui.Button("넣기", "BtnPrimary", "미리보기와 같은 자리에 넣습니다. Ctrl+Z로 되돌림.", "insOk");
        var cancel = ui.Button("취소", "BtnGhost", "", "insCancel");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        void Sync() => ok.IsEnabled = exact.IsChecked == true || topo.IsChecked == true || lines.IsChecked == true;
        foreach (var c in new[] { exact, topo, lines }) { c.Checked += (_, _) => Sync(); c.Unchecked += (_, _) => Sync(); }
        Sync();
        ok.Click += (_, _) =>
        {
            Options = new InsertOptions(exact.IsChecked == true, topo.IsChecked == true, lines.IsChecked == true);
            Log.InsertChoice = (Options.Exact, Options.Topo, Options.Lines);
            DialogResult = true;
        };
        cancel.Click += (_, _) => Close();

        var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MinWidth = 340 };
        stack.Children.Add(new TextBlock { Text = "Civil 최종지표면 · 3D 선 → Revit", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), Foreground = ui["Text"] });
        stack.Children.Add(exact);
        stack.Children.Add(topo);
        stack.Children.Add(lines);
        if (madeCount > 0)
            stack.Children.Add(new TextBlock { Text = $"지난 것 {madeCount}개는 바꿈", Foreground = ui["Sub"], Margin = new Thickness(0, 2, 0, 0) });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        bar.Children.Add(ok);
        bar.Children.Add(cancel);
        stack.Children.Add(bar);
        var page = new Border { Background = ui["Bg"], Child = stack };
        page.Resources.MergedDictionaries.Add(ui.Resources);
        System.Windows.Documents.TextElement.SetFontFamily(page, UiTheme.UiFont);
        System.Windows.Documents.TextElement.SetFontSize(page, 13);
        System.Windows.Documents.TextElement.SetForeground(page, ui["Text"]);
        Content = page;
    }
}
