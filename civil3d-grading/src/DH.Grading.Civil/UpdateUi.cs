using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★[v117.19 · 사내 배포 1123] <b>DHABOUT</b> — 판 정보 창(지금 판 · 지문 · 이번 판 바뀐 점 · 업데이트 확인 · 설치).</summary>
public sealed class AboutCommand
{
    [CommandMethod("DHABOUT", CommandFlags.Session)]
    public void Run() => UpdateUi.ShowInfo();
}

/// <summary>★[v117.19 · 사내 배포 1123 · JACK 1121] 새 판 알림의 화면 쪽 — 시작 뒤 뒤 실에서 확인 · 오른쪽 아래 알림 · 명령줄 한 줄 · [정보] 창 강조.
/// <para><b>주 실을 막지 않는다</b>: 확인(망 · 파일 읽기)은 Task에서, 결과만 주 실 Dispatcher로 넘긴다. 화면 없는 Civil(시험)은 아무것도 안 한다.</para>
/// <para>실패(망 밖 · 경로 없음 · 깨진 json)는 진단 로그 한 줄만 — 사용자에게는 안 알린다(창에서 «업데이트 확인»을 눌렀을 때만 보인다).</para></summary>
internal static class UpdateUi
{
    static Dispatcher? _ui;
    static bool _busy;
    static UpdateCheck.Result? _session;   // 이번 세션에 마지막으로 확인한 결과
    static UpdateInfoWindow? _win;

    /// <summary>시험 이음매 — 참이면 Autodesk 쪽(명령줄 · 주 창 손잡이)을 안 건드리고 <see cref="CommandLineForTest"/>로.</summary>
#pragma warning disable CS0649   // 시험 손잡이가 반사로 넣는다
    internal static bool OffAcadForTest;
    internal static System.Action<string>? CommandLineForTest;
#pragma warning restore CS0649

    internal static event System.Action? Changed;
    internal static bool Busy => _busy;
    internal static UpdateCheck.Result? SessionResult => _session;
    internal static string Current => GradingSettings.Version;

    /// <summary>리본 [정보]에 «새 판» 표시를 할 것인가(기록 기준 — 망 밖에서도).</summary>
    internal static bool RibbonNewer { get; private set; }

    /// <summary>시작 뒤 첫 Idle에서 한 번(RibbonApp) — 하루 한 번만 실제로 확인.</summary>
    internal static void StartupCheck()
    {
        if (SelfCheckLog.Headless) return;
        _ui = Dispatcher.CurrentDispatcher;
        try
        {
            var st = UpdateCheck.LoadState();
            SetRibbon(UpdateCheck.CachedNewer(st, Current));
            if (!UpdateCheck.Due(st, System.DateTime.Now))
            {
                Log($"건너뜀 — 오늘 이미 확인({st.LastCheck:MM-dd HH:mm} · {st.LastStatus})");
                return;
            }
        }
        catch { }
        RunCheck(auto: true);
    }

    /// <summary>확인 한 번 — 뒤 실에서. <paramref name="auto"/>=시작 때(새 판일 때만 알림).</summary>
    internal static void RunCheck(bool auto)
    {
        if (_busy) return;
        _busy = true;
        var ui = _ui ??= Dispatcher.CurrentDispatcher;
        Raise();
        System.Threading.Tasks.Task.Run(async () =>
        {
            UpdateCheck.Result r;
            UpdateCheck.State st;
            List<string> src;
            try { src = Sources(UpdateCheck.LoadState()); } catch { src = new(); }
            // ★위치가 비어 있으면 알림 기능은 조용히 꺼짐 — 시작 때는 기록도 안 남긴다(정보 창에 «업데이트 위치 미설정»)
            if (auto && src.Count == 0)
            {
                Log("업데이트 위치 미설정 — 알림 꺼짐");
                try { _ = ui.BeginInvoke(new System.Action(() => { _busy = false; Raise(); })); } catch { _busy = false; }
                return;
            }
            try
            {
                r = await UpdateCheck.CheckAsync(src, Current).ConfigureAwait(false);
            }
            catch (System.Exception ex) { r = new(UpdateCheck.Status.Unreachable, null, "확인 실패 — " + ex.GetType().Name, 0); }
            st = UpdateCheck.LoadState();
            UpdateCheck.Record(st, r, System.DateTime.Now);
            // ★[위치 이전] latest.json 의 «moved» → 사용자 기록에 새 위치 · 다음부터 거기를 먼저
            if (UpdateCheck.ApplyMoved(st, r.Latest)) Log($"위치 이전 — 새 위치 {st.Sources.Count}곳을 저장(다음 확인부터 먼저 봄)");
            UpdateCheck.SaveState(st);
            Log($"{(auto ? "시작" : "손으로")} · 위치 {r.SourceIndex}/{src.Count} · {r.Status} · {r.Message}");
            try { _ = ui.BeginInvoke(DispatcherPriority.ApplicationIdle, new System.Action(() => Deliver(r, st, auto))); } catch { _busy = false; }
        });
    }

    static void Deliver(UpdateCheck.Result r, UpdateCheck.State st, bool auto)
    {
        _busy = false;
        _session = r;
        try
        {
            SetRibbon(UpdateCheck.CachedNewer(st, Current));
            if (auto && UpdateCheck.ShouldNotify(r, st))
            {
                var lt = r.Latest!;
                CommandLine($"\n[DH 정지] 새 판 v{lt.Version}{(lt.Date.Length > 0 ? " (" + lt.Date + ")" : "")}이 있습니다 — 지금 {SelfCheckLog.Ver}. 리본 [기타 › 정보] 또는 DHABOUT 에서 설치하세요.{(lt.Mandatory ? " ★꼭 설치할 판입니다." : "")}\n");
                UpdateToast.Show(lt);
            }
        }
        catch (System.Exception ex) { Log("알림 실패 — " + ex.GetType().Name); }
        Raise();
    }

    static void Raise() { try { Changed?.Invoke(); } catch { } }

    internal static void Log(string s) { try { DiagLog.Append($"\n■ {System.DateTime.Now:HH:mm:ss} 새 판 확인 — {SelfCheckLog.Clean(s)}\n"); } catch { } }

    static void CommandLine(string s)
    {
        if (OffAcadForTest) { CommandLineForTest?.Invoke(s); return; }
        try { Acad.Write(s); } catch { }
    }

    static void SetRibbon(bool newer)
    {
        RibbonNewer = newer;
        if (OffAcadForTest) return;
        try { Acad.Ribbon(newer); } catch { }
    }

    internal static System.IntPtr AcadHandle()
    {
        if (OffAcadForTest) return System.IntPtr.Zero;
        try { return Acad.Handle(); } catch { return System.IntPtr.Zero; }
    }

    /// <summary>Autodesk 쪽 부름은 여기에만 — 따로 떼어 두어야(인라인 금지) Civil 없는 시험이 이 형들을 안 읽는다.</summary>
    static class Acad
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void Write(string s) => AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(s);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void Ribbon(bool newer) => RibbonApp.MarkUpdate(newer);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static System.IntPtr Handle() => AcadApp.MainWindow.Handle;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void ShowModeless(Window w) => AcadApp.ShowModelessWindow(w);
    }

    /// <summary>실제로 볼 위치 — 사용자 기록(이전 · 직접 바꿈) 먼저, 그다음 설치 기본값.</summary>
    internal static List<string> Sources(UpdateCheck.State st) => UpdateCheck.EffectiveSources(st, InstalledSources());

    internal static IReadOnlyList<string> InstalledSources() { try { return DhKeys.Installed.Update; } catch { return System.Array.Empty<string>(); } }

    /// <summary>[정보] 창 «위치 바꾸기» — 빈 목록이면 사용자 위치를 지우고 설치 기본값만 본다. 틀린 줄이 있으면 저장 안 하고 그 줄을 돌려준다.</summary>
    internal static string? SaveUserSources(IEnumerable<string> lines)
    {
        var list = new List<string>();
        foreach (var raw in lines)
        {
            string t = (raw ?? "").Trim();
            if (t.Length == 0) continue;
            if (!UpdateCheck.ValidSource(t)) return t;
            if (!list.Contains(t, System.StringComparer.OrdinalIgnoreCase)) list.Add(t);
        }
        var st = UpdateCheck.LoadState();
        st.Sources = list;
        st.SourcesOrigin = list.Count > 0 ? "user" : "";
        st.LastCheck = System.DateTime.MinValue;   // 위치를 바꾸면 다음 시작 때 바로 다시 본다
        UpdateCheck.SaveState(st);
        Log(list.Count > 0 ? $"사용자 위치 {list.Count}곳 저장" : "사용자 위치 지움 — 설치 기본값만");
        Raise();
        return null;
    }

    /// <summary>«이 판은 다시 안 알림».</summary>
    internal static void Skip(string version)
    {
        var st = UpdateCheck.LoadState();
        st.SkipVersion = version;
        UpdateCheck.SaveState(st);
        Log("이 판 안 알림 — v" + version);
        Raise();
    }

    // ── 정보 창 ─────────────────────────────────────────

    internal static void ShowInfo()
    {
        if (SelfCheckLog.Headless)
        {
            CommandLine($"\n[DH 정지] {Current} · {SafeStamp()}\n");
            return;
        }
        _ui ??= Dispatcher.CurrentDispatcher;
        try
        {
            if (_win != null && _win.IsLoaded) { _win.Activate(); return; }
            _win = new UpdateInfoWindow(Current, SafeStamp(), InstallDir(), ThisNotes());
            _win.Closed += (_, _) => _win = null;
            if (OffAcadForTest) _win.Show();
            else Acad.ShowModeless(_win);
        }
        catch (System.Exception ex) { Log("정보 창 실패 — " + ex.Message); }
    }

    static string SafeStamp() { try { return SelfCheck.BuildStamp(); } catch { return "?"; } }

    internal static string InstallDir() { try { return System.IO.Path.GetDirectoryName(typeof(UpdateUi).Assembly.Location) ?? ""; } catch { return ""; } }

    /// <summary>이번 판 바뀐 점 — DLL 안 ReleaseNotes.txt의 지금 판 칸.</summary>
    internal static List<string> ThisNotes()
    {
        try
        {
            using var s = typeof(UpdateUi).Assembly.GetManifestResourceStream("DH.Grading.ReleaseNotes.txt");
            if (s == null) return new();
            using var rd = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
            return UpdateCheck.NotesFor(rd.ReadToEnd(), Current);
        }
        catch { return new(); }
    }

    // ── 설치 ───────────────────────────────────────────

    /// <summary>[설치] — 주소면 내려받아 sha256을 맞추고 · 경로(UNC)면 (sha256이 있으면 맞추고) 탐색기로 그 파일을 보여 준다.
    /// <para>여기서 실행하지 않는다 — Civil 3D가 켜져 있으면 DLL이 잠겨 설치가 멈춘다(설치 exe도 acad를 검사한다). «Civil 3D를 닫고 실행»을 안내한다.</para></summary>
    internal static void Install(UpdateCheck.Latest lt, Window owner, System.Action<string, bool> status)
    {
        var ui = Dispatcher.CurrentDispatcher;
        status(UpdateCheck.IsUrl(lt.Installer) ? "내려받는 중…" : "설치 파일 확인 중…", false);
        System.Threading.Tasks.Task.Run(async () =>
        {
            string? path = null, err = null;
            try
            {
                if (UpdateCheck.IsUrl(lt.Installer))
                {
                    string dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "DHGrading", "update");
                    path = await UpdateCheck.DownloadAsync(lt, dir, System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    var t = System.Threading.Tasks.Task.Run(() => System.IO.File.Exists(lt.Installer));
                    if (await System.Threading.Tasks.Task.WhenAny(t, System.Threading.Tasks.Task.Delay(8000)).ConfigureAwait(false) != t || !t.Result)
                        err = "설치 파일을 찾지 못했습니다 — 회사망(공유 폴더) 연결을 확인하세요.";
                    else if (lt.Sha256.Length == 64 && UpdateCheck.Sha256Of(lt.Installer) != lt.Sha256)
                        err = "공유 폴더의 설치 파일 지문(sha256)이 latest.json과 다릅니다 — 올린 사람에게 알려 주세요.";
                    else path = lt.Installer;
                }
            }
            catch (System.Exception ex) { err = ex.Message; }
            Log(err == null ? "설치 파일 준비됨" : "설치 파일 실패 — " + err);
            _ = ui.BeginInvoke(new System.Action(() =>
            {
                if (err != null) { status(err, true); return; }
                status("준비됨 — Civil 3D를 닫고 설치 파일을 실행하세요.", false);
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
                try
                {
                    MessageBox.Show(owner,
                        $"새 판 v{lt.Version} 설치 파일을 열어 두었습니다.\n\n{path}\n\n" +
                        "① Civil 3D를 모두 닫으세요(켜져 있으면 파일이 잠겨 설치가 멈춥니다).\n" +
                        "② 열린 탐색기에서 이 파일을 두 번 누르세요.\n" +
                        "③ 다시 Civil 3D를 켜면 [정보]에 새 판 번호가 보입니다.",
                        "DH 정지 — Civil 3D 닫고 설치", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            }));
        });
    }
}

/// <summary>Civil 3D 화면 오른쪽 아래 알림 — 포커스를 안 뺏는다 · 30초 뒤 저절로 닫힘(마우스가 올라 있으면 기다림).</summary>
internal sealed class UpdateToast : Window
{
    static UpdateToast? _open;
    readonly DispatcherTimer _timer = new() { Interval = System.TimeSpan.FromSeconds(30) };

    internal static void Show(UpdateCheck.Latest lt)
    {
        try { _open?.Close(); } catch { }
        var t = new UpdateToast(lt);
        _open = t;
        t.Closed += (_, _) => { if (_open == t) _open = null; };
        t.Place();
        t.Show();
    }

    internal UpdateToast(UpdateCheck.Latest lt)
    {
        Title = "DH 정지 — 새 판";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = false;
        Width = 360; SizeToContent = SizeToContent.Height;
        Background = DhBrand.Card;
        DhBrand.Apply(this);

        var sp = new StackPanel { Margin = new Thickness(16, 12, 12, 12) };
        var head = new DockPanel();
        var x = new Button { Content = "✕", Width = 24, Height = 24, Padding = new Thickness(0), MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = DhBrand.Sub, ToolTip = "닫기 (나중에)" };
        x.Click += (_, _) => Close();
        DockPanel.SetDock(x, Dock.Right); head.Children.Add(x);
        head.Children.Add(new TextBlock { Text = "DH 정지 — 새 판이 있습니다", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(head);
        sp.Children.Add(new TextBlock
        {
            Text = $"v{lt.Version}{(lt.Date.Length > 0 ? " · " + lt.Date : "")}   (지금 {SelfCheckLog.Ver})",
            FontSize = 12.5, Foreground = DhBrand.Ink, Margin = new Thickness(0, 4, 0, 4),
        });
        if (lt.Mandatory)
            sp.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xEC, 0xE2)), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4),
                Child = new TextBlock { Text = "꼭 설치할 판", Foreground = DhBrand.Warn, FontWeight = FontWeights.Bold, FontSize = 11.5 },
            });
        foreach (var n in lt.Notes.Take(3))
            sp.Children.Add(new TextBlock { Text = "· " + n, TextWrapping = TextWrapping.Wrap, Foreground = DhBrand.Sub, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0) });
        if (lt.Notes.Count > 3)
            sp.Children.Add(new TextBlock { Text = $"… 외 {lt.Notes.Count - 3}개", Foreground = DhBrand.Sub, FontSize = 11.5 });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var more = new Button { Content = "자세히 · 설치", MinWidth = 96, Height = 28, Margin = new Thickness(0, 0, 6, 0) };
        try { if (DhBrand.Skin != null) more.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
        more.Click += (_, _) => { Close(); UpdateUi.ShowInfo(); };
        var later = new Button { Content = "나중에", MinWidth = 64, Height = 28, Margin = new Thickness(0, 0, 6, 0), ToolTip = "내일 다시 알림" };
        later.Click += (_, _) => Close();
        bar.Children.Add(more); bar.Children.Add(later);
        if (!lt.Mandatory)
        {
            var skip = new Button { Content = "이 판 알리지 않기", MinWidth = 64, Height = 28, ToolTip = $"v{lt.Version}은 다시 안 알림(더 새 판이 나오면 알림)" };
            skip.Click += (_, _) => { UpdateUi.Skip(lt.Version); Close(); };
            bar.Children.Add(skip);
        }
        sp.Children.Add(bar);

        Content = new Border
        {
            BorderBrush = DhBrand.Line, BorderThickness = new Thickness(1),
            Child = new DockPanel { Children = { DockTop(DhBrand.Accent()), sp } },
        };

        _timer.Tick += (_, _) => { if (!IsMouseOver) { _timer.Stop(); Close(); } };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    static UIElement DockTop(UIElement e) { DockPanel.SetDock(e, Dock.Top); return e; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(System.IntPtr h, out RECT r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(System.IntPtr h);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    /// <summary>Civil 3D 주 창 오른쪽 아래(상태 막대 위) — 주 창을 못 읽으면 화면 작업 영역 오른쪽 아래.</summary>
    void Place()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        double right = SystemParameters.WorkArea.Right, bottom = SystemParameters.WorkArea.Bottom;
        var h = UpdateUi.AcadHandle();
        if (h != System.IntPtr.Zero)
        {
            try
            {
                new System.Windows.Interop.WindowInteropHelper(this).Owner = h;   // Civil과 함께 최소화 · 앞에 뜸(위로 늘 뜨지는 않음)
                if (GetWindowRect(h, out var r) && r.R > r.L)
                {
                    double s = System.Math.Max(1, GetDpiForWindow(h)) / 96.0;
                    right = r.R / s; bottom = r.B / s - 34;   // 상태 막대 높이만큼 위
                }
            }
            catch { }
        }
        Left = right - Width - 16;
        Top = bottom - 190 - 12;   // 높이는 내용에 맞춰 — 맞춘 뒤 다시 잡는다
        SizeChanged += (_, e) => { if (e.HeightChanged) Top = bottom - ActualHeight - 12; };
    }
}

/// <summary>[정보] 창 — 지금 판 · 날짜 · 지문 · 설치 위치 · 이번 판 바뀐 점 · 업데이트(마지막 확인 · 새 판 강조 · 설치 · 이 판 안 알림).</summary>
internal sealed class UpdateInfoWindow : Window
{
    readonly StackPanel _upd = new();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11.5 };
    readonly Button _check;

    internal UpdateInfoWindow(string version, string stamp, string installDir, List<string> notes)
    {
        Title = "DH 정지 — 정보";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var body = new StackPanel();
        body.Children.Add(H("지금 설치된 판"));
        var v = UpdateCheck.ParseVersion(version);
        string date = UpdateCheck.DateOf(version);
        body.Children.Add(Row("판", v != null ? "v" + UpdateCheck.Show(v) : version, bold: true));
        if (date.Length > 0) body.Children.Add(Row("날짜", date));
        body.Children.Add(Row("빌드 지문", stamp));
        if (installDir.Length > 0) body.Children.Add(Row("설치 위치", installDir));

        body.Children.Add(H("이번 판 바뀐 점"));
        if (notes.Count == 0) body.Children.Add(P("(이 판의 릴리스 노트가 없습니다)", DhBrand.Sub));
        foreach (var n in notes) body.Children.Add(P("· " + n, DhBrand.Ink));

        body.Children.Add(H("업데이트"));
        body.Children.Add(_upd);
        body.Children.Add(_status);
        body.Children.Add(Advanced());

        _check = new Button { Content = "업데이트 확인", MinWidth = 110, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        _check.Click += (_, _) => { _status.Text = ""; UpdateUi.RunCheck(auto: false); };
        var close = new Button { Content = "닫기", MinWidth = 84, Height = 32, IsCancel = true };
        close.Click += (_, _) => Close();
        DhBrand.Dress(this, "DH 정지 — 정보", version, body, _check, close);

        System.Action h = Refresh;
        UpdateUi.Changed += h;
        Closed += (_, _) => UpdateUi.Changed -= h;
        Refresh();
    }

    void Refresh()
    {
        var st = UpdateCheck.LoadState();
        _upd.Children.Clear();
        _check.IsEnabled = !UpdateUi.Busy;
        _check.Content = UpdateUi.Busy ? "확인 중…" : "업데이트 확인";

        if (AdvancedBox != null && !AdvancedBox.IsKeyboardFocusWithin) AdvancedBox.Text = string.Join("\r\n", st.Sources);
        var src = UpdateUi.Sources(st);
        int nUser = st.Sources.Count;
        string where = src.Count == 0 ? "업데이트 위치 미설정 — 새 판 알림 꺼짐"
            : $"{src.Count}곳(차례로 · 모두 3초 안)" + (nUser > 0 ? $" · {(st.SourcesOrigin == "moved" ? "옮긴 위치" : "직접 지정")} {nUser}곳 먼저" : "");
        _upd.Children.Add(Row("확인 위치", where, warn: src.Count == 0));
        _check.IsEnabled = !UpdateUi.Busy && src.Count > 0;
        _upd.Children.Add(Row("마지막 확인", st.LastCheck == System.DateTime.MinValue ? "아직 안 함" : $"{st.LastCheck:yyyy-MM-dd HH:mm} · {Say(st.LastStatus, st.LastMessage)}"));

        var lt = st.Latest;
        bool newer = UpdateCheck.CachedNewer(st, UpdateUi.Current);
        if (newer && lt != null)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock
            {
                Text = $"새 판 v{lt.Version}{(lt.Date.Length > 0 ? " · " + lt.Date : "")}{(lt.Mandatory ? "  ·  꼭 설치할 판" : "")}",
                FontSize = 14, FontWeight = FontWeights.Bold, Foreground = lt.Mandatory ? DhBrand.Warn : DhBrand.Brand,
            });
            foreach (var n in lt.Notes) card.Children.Add(P("· " + n, DhBrand.Ink));
            if (!lt.Mandatory && UpdateCheck.SameVersion(st.SkipVersion, lt.Version))
                card.Children.Add(P("(이 판은 알리지 않기로 했습니다 — 여기서는 그대로 설치할 수 있습니다)", DhBrand.Sub));
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var inst = new Button { Content = UpdateCheck.IsUrl(lt.Installer) ? "설치 파일 받기" : "설치 파일 열기", MinWidth = 120, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            try { if (DhBrand.Skin != null) inst.Style = (Style)DhBrand.Skin["DhPrimary"]; } catch { }
            inst.Click += (_, _) => UpdateUi.Install(lt, this, (msg, bad) => { _status.Text = msg; _status.Foreground = bad ? DhBrand.Warn : DhBrand.Ok; });
            bar.Children.Add(inst);
            if (!lt.Mandatory && !UpdateCheck.SameVersion(st.SkipVersion, lt.Version))
            {
                var skip = new Button { Content = "이 판 알리지 않기", MinWidth = 110, Height = 30 };
                skip.Click += (_, _) => UpdateUi.Skip(lt.Version);
                bar.Children.Add(skip);
            }
            card.Children.Add(bar);
            card.Children.Add(P("설치는 Civil 3D를 모두 닫은 뒤에 합니다(켜져 있으면 파일이 잠겨 설치가 멈춥니다).", DhBrand.Sub));
            _upd.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE8)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x8E)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 8, 0, 0), Child = card,
            });
        }
    }

    /// <summary>«위치 바꾸기(고급)» — 작게 접어 둔다. 한 줄에 하나(\\서버\공유\latest.json · https://…). 비우고 저장 = 설치 기본값만.</summary>
    UIElement Advanced()
    {
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 54, FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join("\r\n", UpdateCheck.LoadState().Sources) };
        var msg = new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        var inst = UpdateUi.InstalledSources();
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap,
            Text = "먼저 볼 위치(한 줄에 하나 · \\\\서버\\공유\\latest.json 또는 https://…). 비우고 저장하면 설치 기본값만 봅니다." });
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { FontSize = 11, Foreground = DhBrand.Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            Text = inst.Count == 0 ? "설치 기본값: 없음" : "설치 기본값: " + string.Join(" · ", inst) });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var save = new Button { Content = "저장", MinWidth = 64, Height = 26, Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) =>
        {
            string? bad = UpdateUi.SaveUserSources(box.Text.Split('\n'));
            msg.Text = bad == null ? "저장했습니다 — [업데이트 확인]으로 바로 볼 수 있습니다." : $"이 줄은 위치가 아닙니다(절대 경로 · UNC · http(s)만): {bad}";
            msg.Foreground = bad == null ? DhBrand.Ok : DhBrand.Warn;
        };
        var clear = new Button { Content = "설치 기본값으로", MinWidth = 64, Height = 26 };
        clear.Click += (_, _) => { box.Text = ""; UpdateUi.SaveUserSources(System.Array.Empty<string>()); msg.Text = "직접 지정한 위치를 지웠습니다."; msg.Foreground = DhBrand.Ok; };
        bar.Children.Add(save); bar.Children.Add(clear);
        panel.Children.Add(bar);
        panel.Children.Add(msg);
        var toggle = new Button { Content = "▸ 위치 바꾸기(고급)", FontSize = 11, Height = 22, MinHeight = 0, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = DhBrand.Sub };
        toggle.Click += (_, _) =>
        {
            bool open = panel.Visibility != Visibility.Visible;
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = (open ? "▾" : "▸") + " 위치 바꾸기(고급)";
        };
        AdvancedPanel = panel;
        AdvancedBox = box;
        var sp = new StackPanel();
        sp.Children.Add(toggle); sp.Children.Add(panel);
        return sp;
    }

    /// <summary>시험 손잡이(그림에서 펼쳐 보이려고).</summary>
    internal StackPanel? AdvancedPanel;
    internal TextBox? AdvancedBox;

    static string Say(string status, string msg) => status switch
    {
        nameof(UpdateCheck.Status.Newer) => msg,
        nameof(UpdateCheck.Status.Same) => "최신 판입니다",
        nameof(UpdateCheck.Status.Older) => "최신 판입니다(올라온 판이 더 낮음)",
        nameof(UpdateCheck.Status.NoSource) => "업데이트 위치 미설정",
        nameof(UpdateCheck.Status.Unreachable) => "확인 못 함 — " + msg,
        nameof(UpdateCheck.Status.BadManifest) => "latest.json 을 못 읽음 — " + msg,
        _ => msg,
    };

    static TextBlock H(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = DhBrand.Brand, Margin = new Thickness(0, 12, 0, 4) };
    static TextBlock P(string t, Brush b) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = b, Margin = new Thickness(0, 1, 0, 1) };

    static Grid Row(string k, string v, bool bold = false, bool warn = false)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var a = new TextBlock { Text = k, Foreground = DhBrand.Sub };
        var b = new TextBlock { Text = v, Foreground = warn ? DhBrand.Warn : DhBrand.Ink, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
        Grid.SetColumn(b, 1);
        g.Children.Add(a); g.Children.Add(b);
        return g;
    }
}
