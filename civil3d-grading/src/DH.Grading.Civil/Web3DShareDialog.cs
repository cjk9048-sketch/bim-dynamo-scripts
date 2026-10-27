using System.Windows;
using System.Windows.Controls;

namespace DH.Grading.Civil;

/// <summary>★★[v107.1 · JACK 1027 «개인 nas» — 시놀로지 · https 주소 있음] <b>3D 웹 공유 설정</b> — NAS 주소 · 폴더 · 웹 주소 · 3D 전용 계정 · [연결 시험] · 올린 것 목록([지우기]).
/// 글은 짧게(JACK 1014) — 자세한 뜻은 풍선 글.</summary>
internal sealed class Web3DShareDialog : Window
{
    readonly TextBox _dsm = Field(), _folder = Field(), _web = Field(), _user = Field();
    readonly PasswordBox _pass = new() { Height = 28, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _msg = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), FontSize = 12 };
    readonly ListBox _list = new() { Height = 150, Margin = new Thickness(0, 0, 0, 6) };
    readonly Web3DShare.Config _cfg;

    static TextBox Field() => new() { Height = 28, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };

    static void Row(Panel p, string label, Control box, string tip)
    {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2), ToolTip = tip });
        box.ToolTip = tip;
        p.Children.Add(box);
    }

    internal Web3DShareDialog()
    {
        Title = "3D 웹 공유";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        _cfg = Web3DShare.Load();

        var root = new StackPanel();
        GradingDialog.AddSection(root, "NAS", "시놀로지 NAS — DSM 웹 API로 올리고 Web Station이 웹으로 보여 줍니다.", first: true);
        Row(root, "DSM 주소", _dsm, "밖에서 DSM에 들어가는 https 주소(포트 포함) — 예 https://이름.synology.me:5001");
        Row(root, "올릴 폴더", _folder, "공유 폴더 web 아래 — 예 /web/3d (없으면 만듭니다)");
        Row(root, "웹 주소", _web, "그 폴더를 보는 주소 — 예 https://이름.synology.me/3d");
        GradingDialog.AddSection(root, "계정", "3D 전용 계정을 권합니다 — 그 폴더에만 쓰기 · 2단계 인증 없음 · File Station 허용.");
        Row(root, "계정", _user, "DSM 계정 이름");
        Row(root, "비밀번호", _pass, "이 PC에만 Windows 암호화로 저장합니다(저장소 · 도면에는 안 들어감)");
        _dsm.Text = _cfg.Dsm; _folder.Text = _cfg.Folder; _web.Text = _cfg.Web; _user.Text = _cfg.User;
        if (_cfg.Pass.Length > 0) _pass.Password = "\u0001keep";   // 저장된 비밀번호 그대로(바꾸면 새로)

        var test = new Button { Content = "연결 시험", MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, __) =>
        {
            test.IsEnabled = false;
            _msg.Foreground = DhBrand.Sub; _msg.Text = "시험 중…";
            try { var c = Collect(); _msg.Text = await Web3DShare.Test(c); _msg.Foreground = _msg.Text.Contains("모두 됩니다") ? DhBrand.Ok : DhBrand.Warn; }
            catch (System.Exception ex) { _msg.Text = ex.Message; _msg.Foreground = DhBrand.Warn; }
            finally { test.IsEnabled = true; }
        };
        root.Children.Add(test);
        root.Children.Add(_msg);

        GradingDialog.AddSection(root, "올린 것", "NAS에 올린 3D — 고르고 [지우기]면 NAS에서도 지웁니다.");
        root.Children.Add(_list);
        var del = new Button { Content = "지우기", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
        del.Click += async (_, __) =>
        {
            if (_list.SelectedItem is not ListBoxItem li || li.Tag is not Web3DShare.Item it) return;
            if (MessageBox.Show(this, $"{it.Dwg} · {it.Time}\n\nNAS에서 지울까요? 이 주소로는 더 못 봅니다.", "3D 웹 공유", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            del.IsEnabled = false;
            try { await Web3DShare.Remove(it); Fill(); _msg.Text = "지웠습니다"; _msg.Foreground = DhBrand.Ok; }
            catch (System.Exception ex) { _msg.Text = ex.Message; _msg.Foreground = DhBrand.Warn; }
            finally { del.IsEnabled = true; }
        };
        root.Children.Add(del);
        Fill();

        var ok = new Button { Content = "저장", IsDefault = true, MinWidth = 96 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, __) =>
        {
            try { Web3DShare.Save(Collect()); DialogResult = true; Close(); }
            catch (System.Exception ex) { _msg.Text = ex.Message; _msg.Foreground = DhBrand.Warn; }
        };
        DhBrand.Dress(this, "3D 웹 공유", "시놀로지 NAS", root, ok, cancel);
    }

    void Fill()
    {
        _list.Items.Clear();
        foreach (var it in Web3DShare.List())
            _list.Items.Add(new ListBoxItem { Content = $"{it.Time} · {it.Dwg} · {it.Mb:F0} MB", Tag = it, ToolTip = it.Url });
    }

    /// <summary>칸 → 설정(비밀번호를 안 바꿨으면 저장된 것 그대로).</summary>
    Web3DShare.Config Collect()
    {
        string folder = _folder.Text.Trim();
        if (!folder.StartsWith("/")) folder = "/" + folder;
        var c = new Web3DShare.Config
        {
            Dsm = _dsm.Text.Trim().TrimEnd('/'), Folder = folder.TrimEnd('/'), Web = _web.Text.Trim().TrimEnd('/'), User = _user.Text.Trim(),
            Pass = _pass.Password == "\u0001keep" ? _cfg.Pass : Web3DShare.Protect(_pass.Password),
        };
        // ★[보안 검토 낮음 1] 주소 · 계정을 바꿨으면 저장된 비밀번호를 그 새 곳으로 보내지 않는다
        if (_pass.Password == "\u0001keep" && (c.Dsm != _cfg.Dsm || c.User != _cfg.User))
            throw new System.InvalidOperationException("DSM 주소나 계정을 바꿨습니다 — 비밀번호를 다시 넣으세요");
        if (!c.Dsm.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) || !c.Web.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            throw new System.InvalidOperationException("DSM 주소 · 웹 주소는 https:// 로 시작해야 합니다(비밀번호가 그대로 오가지 않게)");
        if (!c.Ready) throw new System.InvalidOperationException("칸을 모두 채우세요");
        return c;
    }
}
