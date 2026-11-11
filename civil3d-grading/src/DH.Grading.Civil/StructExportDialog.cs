using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v117.1 · JACK 1111 «기능 자체를 팝업으로 · 팝업 안에 두 점 찍기 · 영역 선택»] [구조물 좌표 내보내기] 창.
/// <para>단추를 누르면 창이 닫히고 부른 명령(<see cref="Commands.StructCommand"/>)이 찍기 · 내보내기를 한 뒤 다시 띄운다(<see cref="AltDialog"/>와 같은 방식) — 창 처리기에서는 도면 일을 안 한다.
/// 글은 짧게(JACK 1014) — 뜻은 풍선 글. AutoCAD 형식을 안 써서 wpfcheck가 그림을 찍는다.</para></summary>
internal sealed class StructExportDialog : Window
{
    internal enum Act { None, PickPts, PickBox, ClearBox, Export }

    /// <summary>창에 보일 값 — 명령이 채운다.</summary>
    internal sealed class View
    {
        public string Name = "", Path = "", DefaultDir = "", DefaultFile = "";
        public bool PathConfirmed;
        public HashSet<string> Include = new(StructRegion3D.Keys);
        /// <summary>«점1 (x, y) · 점2 (x, y) · 31.016 m» — 빈 글 = 안 찍음.</summary>
        public string PtsText = "";
        /// <summary>«120.0 × 80.0 m» — 빈 글 = 네모 없음.</summary>
        public string BoxText = "";
        /// <summary>네모 안 세모 수(지표면이 있을 때).</summary>
        public int Tris;
        /// <summary>내보낼 지표면이 있나 · 그 이름(풍선 글).</summary>
        public bool SurfAvail;
        public string SurfName = "";
        /// <summary>네모 안 선 수(종류마다 — 네모가 있을 때).</summary>
        public Dictionary<string, int> Counts = new();
        /// <summary>노란 알림(기준점이 네모 밖 · 진입도로 빠짐 …).</summary>
        public string Warn = "";
        /// <summary>빨간 알림(지난 내보내기 실패 — 값은 그대로).</summary>
        public string Error = "";
        /// <summary>바닥 작은 글 «EPSG 5186 · 단위 m».</summary>
        public string Foot = "";
        public int Cap = StructRegion3D.MaxTris;
    }

    static readonly SolidColorBrush Amber = Frozen(0x9A, 0x67, 0x00);
    static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    static readonly (string Key, string Label)[] Items =
    {
        ("final", "최종지표면"), ("wall", "옹벽선"), ("bench", "소단선"), ("slope", "사면선"), ("site", "정지 경계"),   // 진입도로 윤곽 = v117.1.1
    };

    internal Act Action { get; private set; } = Act.None;
    internal string NameText => _name.Text.Trim();
    internal string PathText
    {
        get
        {
            string s = _path.Text.Trim().Trim('"');
            if (s.Length > 0 && !s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s += ".json";
            return s;
        }
    }
    internal bool PathConfirmed { get; private set; }
    internal HashSet<string> Include => new(_checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key));

    readonly View _v;
    readonly TextBox _name, _path;
    readonly TextBlock _status;
    readonly Button _ok;
    readonly Dictionary<string, CheckBox> _checks = new();
    bool _settingPath;

    internal StructExportDialog(View v)
    {
        _v = v;
        Title = "구조물 좌표 내보내기";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        PathConfirmed = v.PathConfirmed;
        bool hasBox = v.BoxText.Length > 0;

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Row(string label, UIElement content, string tip)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = DhBrand.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 10), ToolTip = tip };
            Grid.SetRow(lb, row); Grid.SetColumn(lb, 0); g.Children.Add(lb);
            if (content is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 0, 10); fe.ToolTip ??= tip; }
            Grid.SetRow(content, row); Grid.SetColumn(content, 1); g.Children.Add(content);
            row++;
        }
        static Button Btn(string text, string tip) => new() { Content = text, Height = 30, MinWidth = 92, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), ToolTip = tip };
        static TextBlock Note(string text, Brush fg) => new() { Text = text, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        // 이름
        _name = new TextBox { Text = v.Name, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        Row("이름", _name, "Revit에 보일 구조물 이름 · 파일 이름에도 씀");

        // 기준점
        var ptsBtn = Btn("두 점 찍기", "구조물 모서리 두 점(끝점 · 교차점 스냅) — Revit에서도 이 순서로 찍습니다");
        var ptsRow = new DockPanel();
        DockPanel.SetDock(ptsBtn, Dock.Left); ptsRow.Children.Add(ptsBtn);
        ptsRow.Children.Add(Note(v.PtsText.Length > 0 ? v.PtsText : "안 찍음", v.PtsText.Length > 0 ? DhBrand.Ink : DhBrand.Sub));
        Row("기준점", ptsRow, "Civil 도면 좌표(WCS · m)로 적습니다");

        // 영역
        var boxBtn = Btn("네모 찍기", "두 모서리로 네모(도면 WCS 축) — 안의 지표면 · 선을 3D 영역 파일로");
        var clrBtn = Btn("지우기", "네모를 지움 — 점 파일만 내보냄");
        clrBtn.MinWidth = 64; clrBtn.IsEnabled = hasBox;
        var boxRow = new DockPanel();
        DockPanel.SetDock(boxBtn, Dock.Left); boxRow.Children.Add(boxBtn);
        DockPanel.SetDock(clrBtn, Dock.Left); boxRow.Children.Add(clrBtn);
        var boxNote = Note(hasBox ? v.BoxText + (v.SurfAvail ? $" · 세모 {v.Tris.ToString("N0", CultureInfo.InvariantCulture)}" : "") : "없음(점 파일만)", hasBox ? DhBrand.Ink : DhBrand.Sub);
        boxRow.Children.Add(boxNote);
        Row("영역", boxRow, "Revit 애드인에서 3D로 볼 범위");

        // 넣을 것
        var wrap = new WrapPanel();
        foreach (var (key, label) in Items)
        {
            string text = label;
            bool avail = true;
            if (key == "final")
            {
                avail = v.SurfAvail;
                if (!avail) text += "(없음)";
                else if (hasBox) text += " " + v.Tris.ToString("N0", CultureInfo.InvariantCulture);
            }
            else if (hasBox) text += " " + (v.Counts.TryGetValue(key, out int c) ? c : 0).ToString("N0", CultureInfo.InvariantCulture);
            var cb = new CheckBox
            {
                Content = text, IsChecked = v.Include.Contains(key) && avail, IsEnabled = hasBox && avail, Margin = new Thickness(0, 4, 16, 4),
                ToolTip = key == "final" ? (v.SurfName.Length > 0 ? "면: " + v.SurfName : "내보낼 지표면이 없습니다") : "3D 선(네모로 자름)",
            };
            if (key == "final" && !avail) cb.Foreground = DhBrand.Sub;
            cb.Checked += (_, __) => Refresh(); cb.Unchecked += (_, __) => Refresh();
            _checks[key] = cb;
            wrap.Children.Add(cb);
        }
        Row("넣을 것", wrap, "3D 영역 파일에 넣을 것(네모가 있을 때만)");

        // 저장
        _path = new TextBox { Text = v.Path, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        var browse = new Button { Content = "…", Width = 36, Height = 28, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "저장할 파일 고르기" };
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right); pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);
        Row("저장", pathRow, "기준점 파일(*.json) — 3D 영역 파일은 같은 폴더에 «…_3D영역.json»");

        // 바닥 작은 글 · 상태 한 줄
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var foot = new TextBlock { Text = v.Foot, FontSize = 11, Foreground = DhBrand.Sub, Margin = new Thickness(0, -4, 0, 6) };
        Grid.SetRow(foot, row); Grid.SetColumn(foot, 1); g.Children.Add(foot); row++;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MinHeight = 18 };
        Grid.SetRow(_status, row); Grid.SetColumn(_status, 0); Grid.SetColumnSpan(_status, 2); g.Children.Add(_status);

        var (ok, close) = DhBrand.FooterButtons("내보내기");
        close.Content = "닫기";
        _ok = ok;

        ptsBtn.Click += (_, __) => Done(Act.PickPts);
        boxBtn.Click += (_, __) => Done(Act.PickBox);
        clrBtn.Click += (_, __) => Done(Act.ClearBox);
        _name.TextChanged += (_, __) => { PathConfirmed = false; _v.Error = ""; Refresh(); };   // ★[설치 전 검토] 이름을 바꾸면 덮어쓰기를 다시 묻는다
        _path.TextChanged += (_, __) => { if (!_settingPath) PathConfirmed = false; _v.Error = ""; Refresh(); };
        browse.Click += (_, __) =>
        {
            string cur = PathText;
            string dir = "", file = _v.DefaultFile;
            try { if (cur.Length > 0) { dir = System.IO.Path.GetDirectoryName(cur) ?? ""; file = System.IO.Path.GetFileName(cur); } } catch { }
            if (dir.Length == 0 || !System.IO.Directory.Exists(dir)) dir = _v.DefaultDir;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "구조물 기준점 파일(Revit 애드인용)", Filter = "구조물 기준점 (*.json)|*.json", FileName = file, InitialDirectory = dir,
                DefaultExt = ".json", AddExtension = true, OverwritePrompt = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            _settingPath = true; _path.Text = dlg.FileName; _settingPath = false;
            PathConfirmed = true;
        };
        ok.Click += (_, __) =>
        {
            if (StructRegion3D.WhyNot(NameText.Length > 0, _v.PtsText.Length > 0, PathText.Length > 0, _v.BoxText.Length > 0, FinalOn, _v.Tris, Chosen(), _v.Cap).Length > 0) return;
            // ★[계획 9절 L7] 직접 친 경로에 파일이 있으면 덮어쓸지 묻는다([…]로 고른 것은 그 창이 이미 물음)
            if (!PathConfirmed && System.IO.File.Exists(PathText)
                && MessageBox.Show(this, $"{PathText}\n\n이미 있습니다. 덮어쓸까요?", "구조물 좌표 내보내기", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Done(Act.Export);
        };
        DhBrand.Dress(this, "구조물 좌표 내보내기", null, g, ok, close);
        Refresh();
    }

    bool FinalOn => _v.SurfAvail && _checks.TryGetValue("final", out var cb) && cb.IsChecked == true;

    int Chosen()
    {
        int n = 0;
        foreach (var (k, cb) in _checks)
        {
            if (cb.IsChecked != true) continue;
            if (k == "final") { if (_v.SurfAvail) n += _v.Tris; }
            else n += _v.Counts.TryGetValue(k, out int c) ? c : 0;
        }
        return n;
    }

    /// <summary>[내보내기] 켜짐 · 상태 한 줄 — 규칙은 Core 순수 함수(<see cref="StructRegion3D.WhyNot"/> · S247 시험).</summary>
    void Refresh()
    {
        if (_ok == null || _status == null) return;
        string why = StructRegion3D.WhyNot(NameText.Length > 0, _v.PtsText.Length > 0, PathText.Length > 0, _v.BoxText.Length > 0, FinalOn, _v.Tris, Chosen(), _v.Cap);
        _ok.IsEnabled = why.Length == 0;
        if (_v.Error.Length > 0) { _status.Text = _v.Error; _status.Foreground = DhBrand.Warn; }
        else if (why.Length > 0) { _status.Text = why; _status.Foreground = why.Contains("줄이세요") || why.Contains("없음") ? DhBrand.Warn : DhBrand.Sub; }
        else if (_v.Warn.Length > 0) { _status.Text = _v.Warn; _status.Foreground = Amber; }
        else { _status.Text = ""; }
    }

    void Done(Act a)
    {
        Action = a;
        DialogResult = true;
    }
}
