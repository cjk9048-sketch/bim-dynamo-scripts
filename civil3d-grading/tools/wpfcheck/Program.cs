using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // 1) XAML 자체가 파싱되는가 — 실패하면 창이 기본 모양으로 뜬다(조용히).
        string xaml = (string)typeof(DH.Grading.Civil.DhBrand)
            .GetField("SkinXaml", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        ResourceDictionary rd;
        try { rd = (ResourceDictionary)XamlReader.Parse(xaml); }
        catch (Exception ex) { Console.WriteLine("XAML 파싱 실패 — " + ex.Message); return 1; }
        Console.WriteLine($"XAML 파싱 통과 · 항목 {rd.Count}개");

        // 2) 꼭 있어야 할 이름표
        foreach (var key in new object[] { "DhPrimary", "DhComboToggle", "DhSliderThumb" })
            if (!rd.Contains(key)) { Console.WriteLine("이름표 없음 — " + key); return 1; }

        // 3) 암묵 스타일이 실제 컨트롤에 걸리고 템플릿이 만들어지는가
        //    (파싱만 되고 적용에서 터지는 경우를 잡는다 — 그게 진짜 사고다)
        int made = 0;
        var win = new Window { Width = 400, Height = 300, ShowActivated = false };
        win.Resources.MergedDictionaries.Add(rd);
        var sp = new StackPanel();
        var cb = new ComboBox();
        cb.Items.Add("가"); cb.Items.Add("나"); cb.SelectedIndex = 0;
        var sl = new Slider { Minimum = 0, Maximum = 4, TickFrequency = 1,
                              IsSnapToTickEnabled = true,
                              TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                              Width = 120, Value = 2 };
        var ok = new Button { Content = "저장", Style = (Style)rd["DhPrimary"] };
        Control[] all = { new TextBox { Text = "12.5" }, cb, new Button { Content = "취소" }, ok,
                          new RadioButton { Content = "자동", IsChecked = true },
                          new RadioButton { Content = "수동" },
                          new CheckBox { Content = "결과만", IsChecked = true }, sl };
        foreach (var c in all) sp.Children.Add(c);
        win.Content = sp;
        try
        {
            win.Show();
            win.UpdateLayout();
            foreach (var c in all)
                if (c.Template != null && VisualTreeHelperCount(c) > 0) made++;
            win.Close();
        }
        catch (Exception ex) { Console.WriteLine("적용 실패 — " + ex); return 1; }

        Console.WriteLine($"컨트롤 {made}/{all.Length}개가 새 모양으로 그려짐");
        if (made != all.Length) { Console.WriteLine("일부가 안 그려졌다"); return 1; }

        // 4) 콤보 목록이 실제로 펼쳐지는가 — Popup 템플릿이 잘못되면 여기서 터진다
        try { cb.IsDropDownOpen = true; win.UpdateLayout(); cb.IsDropDownOpen = false; }
        catch (Exception ex) { Console.WriteLine("콤보 펼침 실패 — " + ex.Message); return 1; }
        Console.WriteLine("콤보 펼침 통과");

        // 5) 모양표가 슬라이더 <b>동작</b>을 바꾸지 않았는지 — 기본 모양과 <b>나란히</b> 잰다.
        //    (IsSnapToTickEnabled는 사람이 끌 때 붙는 것이지 값을 직접 넣을 때가 아니다.
        //     그러니 "2.4가 2로 안 붙는다"는 것만으로는 아무 것도 말할 수 없다 — 견줘야 안다.)
        var bare = new Slider { Minimum = 0, Maximum = 4, TickFrequency = 1,
                                IsSnapToTickEnabled = true, Width = 120, Value = 2 };
        var bareWin = new Window { Width = 200, Height = 100, ShowActivated = false, Content = bare };
        bareWin.Show(); bareWin.UpdateLayout();
        bare.Value = 2.4; sl.Value = 2.4;
        double vBare = bare.Value, vSkin = sl.Value;
        bare.Value = 9;   sl.Value = 9;          // 범위 넘기기도 같아야 한다
        double cBare = bare.Value, cSkin = sl.Value;
        bareWin.Close();
        Console.WriteLine($"슬라이더 견주기 — 2.4→ 기본 {vBare} · 새모양 {vSkin} / 9→ 기본 {cBare} · 새모양 {cSkin}");
        if (Math.Abs(vBare - vSkin) > 1e-9 || Math.Abs(cBare - cSkin) > 1e-9)
        { Console.WriteLine("모양표가 동작을 바꿨다"); return 1; }
        Console.WriteLine("슬라이더 동작 동일 — 통과");

        if (DiagCheck.Run() != 0) return 1;
        int rc = CloseCheck.Run();
        if (rc != 0) return rc;
        Console.WriteLine("== 화면 모양표 전부 통과 ==");
        return 0;
    }

    private static int VisualTreeHelperCount(System.Windows.DependencyObject d)
        => System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
}
