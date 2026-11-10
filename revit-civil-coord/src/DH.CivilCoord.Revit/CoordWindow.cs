using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// «Civil 좌표 맞춤» 창(JACK 1110 인터뷰에서 고른 꼴): 점 1 · 점 2 를 Revit 에서 찍고 Civil X(동) · Y(북)를 넣음 → 거리 · 회전 확인 →
/// [IFC 내보내기] · [공유 좌표에 적용]. 점을 찍을 때는 창을 닫고(Revit 이 마우스를 받게) 찍은 뒤 다시 연다(<see cref="CoordCommand"/>).
/// 설명 글은 두지 않는다(JACK 1014 — 이름표 · 숫자 · 단추만 · 자세한 것은 풍선 글).
/// </summary>
internal sealed class CoordWindow : Window
{
    public enum Act { None, Pick1, Pick2, Export, Apply }

    public Act Action { get; private set; } = Act.None;
    public string ExportPath { get; private set; } = "";
    public FitState State { get; }

    private readonly UiTheme _ui;
    private readonly string _defaultPath;
    private readonly TextBox _c1x, _c1y, _c2x, _c2y, _dz;
    private readonly TextBlock _check = new();
    private readonly Border _checkPill = new();
    private readonly Button _export, _apply;
    // Civil 기준점 파일(JACK 1110 «타이핑 말고 Civil에서 받아오기»)
    private readonly TextBlock _civilInfo = new(), _h1 = new(), _h2 = new();
    private readonly Border _civilPill = new();
    private string _civilError = "";
    private bool _loading;

    private static double? s_left, s_top;   // 다시 열 때 같은 자리

    public CoordWindow(FitState state, LevelHeights heights, SharedCoords.Info shared, string status, string statusKind, string defaultPath, nint owner, bool dark)
    {
        State = state.Clone();
        _defaultPath = defaultPath;
        _ui = new UiTheme(dark);

        Title = Dialogs.Title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        if (s_left is double l && s_top is double t) { WindowStartupLocation = WindowStartupLocation.Manual; Left = l; Top = t; }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ = new WindowInteropHelper(this) { Owner = owner };
        if (dark) SourceInitialized += (_, _) => DarkTitleBar();
        Closed += (_, _) => { s_left = Left; s_top = Top; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        var page = new Border { Background = _ui["Bg"], Child = root };
        page.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement(page);
        Background = _ui["Bg"];
        Content = page;

        // ── 두 점
        var card = Card();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _c1x = Coord(State.C1X, "Civil 도면 X(동쪽) m — 이 점의 Civil 좌표", "c1x");
        _c1y = Coord(State.C1Y, "Civil 도면 Y(북쪽) m", "c1y");
        _c2x = Coord(State.C2X, "Civil 도면 X(동쪽) m", "c2x");
        _c2y = Coord(State.C2Y, "Civil 도면 Y(북쪽) m", "c2y");
        // Civil 줄: [Civil 파일 불러오기] · «이름» · 도면 · EPSG · (손으로 고침)
        var civil = Row(); civil.Margin = new Thickness(0, 0, 0, 12);
        var cl = Label("Civil", 52); cl.FontWeight = FontWeights.SemiBold;
        civil.Children.Add(cl);
        var load = _ui.Button("Civil 파일 불러오기", "BtnSoft",
            "Civil 3D(DH 부지)에서 두 점을 찍어 내보낸 기준점 파일(.json)을 읽어 점 1 · 2의 X · Y를 채웁니다. 단위 · 좌표계 · 점 개수 · 손으로 고친 흔적을 검사합니다.", "loadCivil", "");
        load.Click += (_, _) => OnLoadCivil();
        civil.Children.Add(load);
        _civilPill.Margin = new Thickness(10, 0, 0, 0);   // 딱지는 단추 바로 옆(긴 이름에 밀려 잘리지 않게 — 1110 미리보기)
        civil.Children.Add(_civilPill);
        _civilInfo.Margin = new Thickness(10, 0, 0, 0); _civilInfo.VerticalAlignment = VerticalAlignment.Center;
        _civilInfo.TextTrimming = TextTrimming.CharacterEllipsis; _civilInfo.MaxWidth = 280;
        civil.Children.Add(_civilInfo);
        _h1.Foreground = _ui["Sub"]; _h2.Foreground = _ui["Sub"];
        AddPoint(grid, 0, "점 1", State.R1, _c1x, _c1y, Act.Pick1, _h1);
        AddPoint(grid, 3, "점 2", State.R2, _c2x, _c2y, Act.Pick2, _h2);
        card.Child = new StackPanel { Children = { civil, grid } };
        root.Children.Add(card);

        // ── 높이
        var hCard = Card();
        var hLine = Row();
        hLine.Children.Add(Label("높이", 52));
        hLine.Children.Add(new TextBlock { Text = heights.Label, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Text"] });
        hLine.Children.Add(new TextBlock { Text = "  +", Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        _dz = _ui.Input("0", "레벨 높이에 더할 값(m) — 위로 +", "dz");
        _dz.Text = State.Dz; _dz.Width = 80; _dz.TextAlignment = TextAlignment.Right;
        hLine.Children.Add(_dz);
        hLine.Children.Add(new TextBlock { Text = " m", VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        if (heights.Note.Length > 0) { var p = Pill(heights.Note, "Warn"); p.Margin = new Thickness(10, 0, 0, 0); hLine.Children.Add(p); }
        hCard.Child = hLine;
        root.Children.Add(hCard);

        // ── 확인(거리 · 회전)
        var check = Row(); check.Margin = new Thickness(2, 10, 0, 0);
        check.Children.Add(Label("확인", 76));
        _check.VerticalAlignment = VerticalAlignment.Center; _check.Foreground = _ui["Text"];
        check.Children.Add(_check);
        _checkPill.Margin = new Thickness(10, 0, 0, 0);
        check.Children.Add(_checkPill);
        root.Children.Add(check);

        // ── 지금 공유 좌표(보기만 — 단추와 떨어뜨림)
        var sh = Row(); sh.Margin = new Thickness(2, 8, 0, 0);
        sh.Children.Add(Label("공유 좌표", 76));
        sh.Children.Add(new TextBlock { Text = shared.Text, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] });
        if (shared.Pill.Length > 0) { var p = Pill(shared.Pill, shared.PillKind); p.Margin = new Thickness(10, 0, 0, 0); sh.Children.Add(p); }
        _ui.Tip(sh, "Revit 프로젝트 기준점의 지금 공유 좌표(관리 → 좌표). [공유 좌표에 적용]을 누르면 이 값이 Civil 좌표가 됩니다.");
        root.Children.Add(sh);

        // ── 마지막 일의 결과
        if (status.Length > 0)
        {
            var (bg, fg) = statusKind switch { "Good" => ("GoodBg", "GoodFg"), "Bad" => ("BadBg", "BadFg"), "Warn" => ("WarnBg", "WarnFg"), _ => ("NeutralBg", "NeutralFg") };
            root.Children.Add(new Border
            {
                Background = _ui[bg], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 12, 0, 0),
                Child = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Foreground = _ui[fg] },
            });
        }

        // ── 단추
        var bar = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        _apply = _ui.Button("공유 좌표에 적용", "BtnSecondary",
            "같은 두 점 값으로 Revit 공유 좌표(프로젝트 기준점 · 측량점)를 Civil 좌표로 바꿉니다 — Revit IFC · DWG · NWC를 «공유 좌표»로 내보내면 Civil · 나비스웍스에 맞습니다. Ctrl+Z로 되돌림.", "apply");
        _apply.Click += (_, _) => { if (Ready()) { Action = Act.Apply; Close(); } };
        DockPanel.SetDock(_apply, Dock.Left);
        bar.Children.Add(_apply);
        var close = _ui.Button("닫기", "BtnGhost", "", "close");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        _export = _ui.Button("IFC 내보내기", "BtnPrimary", "지금 3D 뷰에 보이는 부재를 Civil 좌표(m) IFC로 내보냅니다(3D 뷰가 아니면 기본 3D 뷰).", "export");
        _export.Margin = new Thickness(0, 0, 8, 0);
        _export.Click += (_, _) => OnExport();
        DockPanel.SetDock(_export, Dock.Right);
        bar.Children.Add(_export);
        root.Children.Add(bar);

        foreach (var box in new[] { _c1x, _c1y, _c2x, _c2y, _dz }) box.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    // ── 값 · 확인 ──────────────────────────────────────────────────────────────

    private void Capture()
    {
        State.C1X = _c1x.Text; State.C1Y = _c1y.Text; State.C2X = _c2x.Text; State.C2Y = _c2y.Text; State.Dz = _dz.Text;
    }

    private void Refresh()
    {
        if (_loading) return;
        Capture();
        RefreshCivil();
        var fit = State.Fit();
        string? problem = !State.DzValid ? "높이 칸이 숫자가 아닙니다" : fit?.Problem;
        if (fit == null && problem == null)
        {
            _check.Text = "거리 —   회전 —";
            SetPill(_checkPill, "", "");
        }
        else if (problem != null)
        {
            _check.Text = problem;
            SetPill(_checkPill, "못 씀", "Bad");
        }
        else
        {
            double err = fit!.DistanceError;
            _check.Text = $"거리  Revit {fit.RevitDistance:0.000} · Civil {fit.CivilDistance:0.000} m (차 {err * 1000:+0;-0;0} mm)    회전 {fit.AngleDegrees:+0.00;-0.00;0.00}°";
            if (Math.Abs(err) <= 0.01) SetPill(_checkPill, "맞음", "Good");
            else if (Math.Abs(err) <= 0.10) SetPill(_checkPill, "거리 차 확인", "Warn");
            else SetPill(_checkPill, "점 확인", "Bad");
        }
        bool ok = fit != null && problem == null;
        _export.IsEnabled = ok;
        _apply.IsEnabled = ok;
    }

    private void RefreshCivil()
    {
        if (_civilError.Length > 0)
        {
            _civilInfo.Text = _civilError; _civilInfo.Foreground = _ui["BadFg"];
            _ui.Tip(_civilInfo, _civilError);
            SetPill(_civilPill, "못 읽음", "Bad");
            return;
        }
        _civilInfo.Foreground = _ui["Sub"];
        if (State.CivilFile.Length == 0)
        {
            _civilInfo.Text = ""; SetPill(_civilPill, "", "");
            _h1.Text = ""; _h2.Text = "";
            _h1.Visibility = Visibility.Collapsed; _h2.Visibility = Visibility.Collapsed;
            return;
        }
        _civilInfo.Text = State.CivilSummary.Length > 0 ? State.CivilSummary : Path.GetFileName(State.CivilFile);
        _ui.Tip(_civilInfo, State.CivilFile);
        bool same = State.CivilUntouched;
        SetPill(_civilPill, same ? "" : "손으로 고침", same ? "" : "Warn");
        _h1.Text = State.CivilH1.Length > 0 ? "Civil " + State.CivilH1 : "";
        _h2.Text = State.CivilH2.Length > 0 ? "Civil " + State.CivilH2 : "";
        _h1.Visibility = _h1.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _h2.Visibility = _h2.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLoadCivil()
    {
        string last = Log.LastCivilFolder;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Civil 기준점 파일 불러오기",
            Filter = "Civil 기준점 (*.json)|*.json|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
        };
        string dir = last.Length > 0 && Directory.Exists(last) ? last : Path.GetDirectoryName(State.CivilFile) ?? "";
        if (dir.Length > 0 && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName;
        Log.LastCivilFolder = Path.GetDirectoryName(path) ?? "";
        var cp = CivilPoints.Read(path, out string? err);
        if (err != null)
        {
            _civilError = "원인: " + err;
            Log.Line($"Civil 파일 못 읽음\t{path}\t{err}");
            RefreshCivil();
            return;
        }
        _civilError = "";
        var tmp = State.Clone();
        tmp.ApplyCivil(cp, path);
        _loading = true;   // 칸 넷을 다 넣은 뒤 한 번만 읽음(하나씩 넣다 옛 칸 값이 섞이지 않게)
        try { _c1x.Text = tmp.C1X; _c1y.Text = tmp.C1Y; _c2x.Text = tmp.C2X; _c2y.Text = tmp.C2Y; }
        finally { _loading = false; }
        State.CivilFile = tmp.CivilFile; State.CivilSummary = tmp.CivilSummary;
        State.CivilH1 = tmp.CivilH1; State.CivilH2 = tmp.CivilH2; State.CivilLoaded = tmp.CivilLoaded;
        Log.Line($"Civil 파일\t{path}\t{cp.Summary}\t점1 {cp.Points[0].X:0.0000},{cp.Points[0].Y:0.0000}\t점2 {cp.Points[1].X:0.0000},{cp.Points[1].Y:0.0000}" +
                 $"\t단위 m(도면 INSUNITS {cp.Insunits?.ToString() ?? "?"}{(cp.UnitAssumed ? " · 단위 없음 → m로 봄" : "")})\tUCS=WCS {cp.UcsWasWorld?.ToString() ?? "?"}\t{cp.CivilApp} {cp.Addin} {cp.Created}");
        Refresh();
    }

    private bool Ready()
    {
        Capture();
        var fit = State.Fit();
        return fit != null && fit.Problem == null && State.DzValid;
    }

    private void OnExport()
    {
        if (!Ready()) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "IFC 내보내기 — Civil 좌표",
            Filter = "IFC (*.ifc)|*.ifc",
            DefaultExt = ".ifc",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(_defaultPath),
        };
        string dir = Path.GetDirectoryName(_defaultPath) ?? "";
        if (dir.Length > 0 && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) != true) return;
        ExportPath = dlg.FileName;
        Action = Act.Export;
        Close();
    }

    // ── 부품 ──────────────────────────────────────────────────────────────────

    private void AddPoint(Grid grid, int row, string name, double[]? revit, TextBox x, TextBox y, Act pick, TextBlock heights)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lab = Label(name, 52); lab.FontWeight = FontWeights.SemiBold;
        Grid.SetRow(lab, row); Grid.SetColumn(lab, 0);
        grid.Children.Add(lab);

        var top = Row(); top.Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0);
        Grid.SetRow(top, row); Grid.SetColumn(top, 1);
        if (row != 0) lab.Margin = new Thickness(0, 12, 0, 0);
        var btn = _ui.Button("Revit에서 찍기", "BtnSoft", "Revit 평면도에서 구조물 모서리를 찍습니다(끝점 · 교차점에 붙음). Esc = 그만", pick == Act.Pick1 ? "pick1" : "pick2", "");   // 위치 핀(Segoe MDL2 MapPin)
        btn.Click += (_, _) => { Capture(); Action = pick; Close(); };
        top.Children.Add(btn);
        bool has = revit is { Length: >= 2 };
        var p = Pill(has ? "찍음" : "아직", has ? "Good" : "");
        p.Margin = new Thickness(10, 0, 0, 0);
        top.Children.Add(p);
        if (has)
            top.Children.Add(new TextBlock
            {
                Text = $"Revit {revit![0]:0.000}, {revit[1]:0.000} m", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"],
            });
        grid.Children.Add(top);

        var bottom = Row(); bottom.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(bottom, row + 1); Grid.SetColumn(bottom, 1);
        bottom.Children.Add(Small("X(동)"));
        bottom.Children.Add(x);
        bottom.Children.Add(Small("Y(북)", 14));
        bottom.Children.Add(y);
        bottom.Children.Add(Small("m", 6));
        grid.Children.Add(bottom);

        // Civil 지반고(불러온 파일에 있으면 · 없으면 접힘) — 구조물 바닥 높이 눈 확인용
        heights.Margin = new Thickness(46, 5, 0, 0);
        Grid.SetRow(heights, row + 2); Grid.SetColumn(heights, 1);
        grid.Children.Add(heights);
    }

    private TextBox Coord(string text, string tip, string id)
    {
        var box = _ui.Input("", tip, id);
        box.Text = text; box.Width = 150; box.TextAlignment = TextAlignment.Right;
        return box;
    }

    private Border Card() => new()
    {
        Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10),
    };

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private TextBlock Label(string text, double width) => new() { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Text"] };

    private TextBlock Small(string text, double left = 0) => new() { Text = text, Margin = new Thickness(left, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = _ui["Sub"] };

    private Border Pill(string text, string kind)
    {
        var b = new Border();
        SetPill(b, text, kind);
        return b;
    }

    private void SetPill(Border b, string text, string kind)
    {
        if (text.Length == 0) { b.Visibility = Visibility.Collapsed; return; }
        var (bg, fg) = kind switch { "Good" => ("GoodBg", "GoodFg"), "Bad" => ("BadBg", "BadFg"), "Warn" => ("WarnBg", "WarnFg"), _ => ("NeutralBg", "NeutralFg") };
        b.Visibility = Visibility.Visible;
        b.Background = _ui[bg];
        b.CornerRadius = new CornerRadius(10);
        b.Padding = new Thickness(9, 2, 9, 3);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Child = new TextBlock { Text = text, Foreground = _ui[fg], FontSize = 12 };
    }

    private void TextElement(FrameworkElement e)
    {
        System.Windows.Documents.TextElement.SetFontFamily(e, UiTheme.UiFont);
        System.Windows.Documents.TextElement.SetFontSize(e, 13);
        System.Windows.Documents.TextElement.SetForeground(e, _ui["Text"]);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private void DarkTitleBar()
    {
        try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int)); }
        catch { }
    }
}
