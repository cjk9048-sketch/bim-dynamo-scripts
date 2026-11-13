using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★[JACK 1103 «3D 뷰 도킹팝업창 생길 때 기존 팝업에 겹치게 생기는데 그렇지 않게 해줘. 나머지 도킹팝업창도 마찬가지야» → «오른쪽에 나란히»]
/// <b>DH 도킹창 여섯(3D 보기 · 진입도로 · 계획부지 정지 · 지층 구성 · 구조물 터파기 · 서버 지표면)이 오른쪽에 붙는 법 — 한 곳.</b>
///
/// <para><b>acmgd.dll을 풀어 본 사실</b>(1103 · ILSpy — 짐작 아님):</para>
/// <para>① <c>Dock</c> 읽기는 <b>어느 쪽인지 알려 주지 않는다</b> — 떠 있으면 <c>None</c>, 붙어 있으면 늘 <c>Left|Top|Right|Bottom</c>.
///   그래서 종전 로그의 «도킹 Left, Top, Right, Bottom ⚠우측에 안 붙었다»는 <b>붙어 있다는 뜻</b>이었다(헛경보).
///   어느 쪽인지는 창 자리(AutoCAD 창 가운데보다 오른쪽인가)로 가린다.</para>
/// <para>② <c>Dock = Right</c> 쓰기는 <b>그 순간 팔레트 창의 화면 사각형</b>(<c>GetWindowRect</c>)을 그대로
///   <c>CAdUiDockControlBar::DockControlBar(오른쪽 도킹 띠, 사각형)</c>에 넘긴다 — 어디에 붙을지는 <b>창이 지금 있는 자리</b>가 정한다.
///   새 창이 이미 붙은 창 위에 떠 있다가 붙으면 그 칸에 끼어든다 = 겹침.</para>
///
/// <para>→ <b>다른 DH 창이 오른쪽에 붙어 있으면</b>, 새 창을 먼저 그 칸들 <b>바로 왼쪽</b>(도면 쪽)에 띄워 놓고(<c>FloatControl</c>) 그다음에 붙인다 —
///   사람이 끌어다 놓는 것과 같은 길이다. MFC 도킹 띠는 사각형 가운데가 띠 왼쪽 끝보다 왼쪽이면 <b>새 칸을 맨 왼쪽에</b> 만든다.
///   ★AutoCAD 안쪽(네이티브)이 MFC 규칙을 그대로 따르는지는 <b>못 쟀다</b> — 그래서 고른 길 · 사각형 · 자리 잡은 뒤 자리를 로그에 남긴다(<c>[도킹]</c> 줄).</para>
///
/// <para>★<b>도킹은 보인 뒤에</b>(§56 · 0831 교훈) — 안 뜬 팔레트는 첫 <c>Visible</c>에서 저장된 자리로 되살리므로, 그 전의 <c>Dock</c>은 덮인다.
/// ★서로 닫는 창(진입도로 ↔ 계획부지 정지 · 구조물 터파기)은 부르는 쪽이 <b>먼저</b> 숨긴다 — 숨은 창은 이웃으로 안 센다.</para></summary>
internal static class DhDock
{
    /// <summary>MFC 오른쪽 도킹 띠의 창 번호(<c>AFX_IDW_DOCKBAR_RIGHT</c> — <c>Dock</c> 쓰기가 넘기는 값과 같다).</summary>
    const int DockBarRight = 0xE81D;
    /// <summary>새 창을 띄워 둘 자리 — 이웃 칸 왼쪽 끝에서 이만큼 더 왼쪽.</summary>
    const int Gap = 12;
    /// <summary>★[v117.14.1 · JACK 1112] 다른 창이 떠도 안 닫는 창 — 3D 보기.</summary>
    const string Keep3D = "3D 보기";

    /// <summary>지금까지 연 DH 도킹창(이름 · 팔레트) — 이웃을 셀 때 쓴다.</summary>
    static readonly List<(PaletteSet Ps, string Name)> _all = new();

    /// <summary>창을 보이고 오른쪽에 붙인다 — 다른 DH 창이 오른쪽에 붙어 있으면 그 왼쪽 옆에 나란히.
    /// <para>이미 오른쪽에 붙어 있고 이웃과 안 겹치면 <b>건드리지 않는다</b>(사람이 옮긴 칸 · 폭 그대로).</para></summary>
    /// <param name="width">새 칸 폭(px) — 0이면 지금 폭.</param>
    internal static void ShowRight(PaletteSet ps, string name, int width = 0)
    {
        Register(ps, name);
        // ★[v117.14.1 · JACK 1112 «종횡단도 누르면 팝업 뜨잖아 그럼 기존 도킹팝업은 닫아. 3D 보기 팝업을 제외하고는 도킹팝업은 다른 게 뜨면 닫히게»]
        //   새 창을 보이기 전에 다른 DH 도킹창을 숨긴다(숨기기만 — 고른 것 · 값 · 세션은 그대로) · 3D 보기는 남김(열 때도 남을 닫지 않음)
        string closed = "";
        if (name != Keep3D)
            foreach (var o in _all)
            {
                if (ReferenceEquals(o.Ps, ps) || o.Name == Keep3D) continue;
                try { if (o.Ps.Visible) { o.Ps.Visible = false; closed += (closed.Length > 0 ? " · " : "") + "«" + o.Name + "»"; } } catch { }
            }
        ps.Visible = true;
        string how, plan = closed.Length > 0 ? " · 닫음 " + closed : "";
        try
        {
            var frame = FrameRect();
            var others = Neighbours(ps, frame);
            var me = RectOf(ps);
            bool docked = IsDocked(ps);
            bool right = docked && me != null && frame != null && Cx(me.Value) > Cx(frame.Value);
            string? hit = me == null ? null : HitName(me.Value, others);
            if (right && hit == null) how = "그대로(이미 우측)";
            else if (others.Count == 0)
            {
                ps.Dock = DockSides.Right;
                how = docked ? (right ? "우측 다시" : "좌측 → 우측") : "떠 있음 → 우측";
            }
            else
            {
                // 이웃 칸들 왼쪽 끝 — 오른쪽 도킹 띠가 보이면 그 왼쪽 끝도(Autodesk 창이 더 왼쪽에 붙어 있을 수 있다)
                int left = others.Min(o => o.R.L), top = others.Min(o => o.R.T), bottom = others.Max(o => o.R.B);
                var bar = DockBarRect();
                if (bar != null && bar.Value.W > 0) left = Math.Min(left, bar.Value.L);
                int w = width > 0 ? width : (me?.W ?? ps.Size.Width);
                int r = left - Gap, l = r - Math.Max(50, w);
                if (frame != null && l < frame.Value.L + 4) l = Math.Min(frame.Value.L + 4, r - 50);
                plan += $" · 띄운 자리 {l},{top}~{r},{bottom}(가운데 x {(l + r) / 2} < 띠 왼쪽 {left})"
                     + (bar != null ? $" · 도킹 띠 {bar.Value}" : " · 도킹 띠 못 찾음");
                ps.FloatControl(new System.Windows.Rect(l, top, r - l, Math.Max(100, bottom - top)));
                ps.Dock = DockSides.Right;
                how = "나란히(왼쪽 옆 — 이웃 " + string.Join(" · ", others.Select(o => "«" + o.Name + "»")) + ")"
                    + (hit != null ? $" · 전에 «{hit}»와 겹쳐 있었음" : "");
            }
        }
        catch (System.Exception ex)
        {
            how = "실패 — " + ex.Message + " → 종전처럼 우측";
            try { ps.Dock = DockSides.Right; } catch { }
        }
        try { DiagLog.Append($"\n[도킹] «{name}» 열기 — {how}{plan} · 지금 {Where(ps)}"); } catch { }
        LogSettled(ps, name);
    }

    /// <summary>창이 어디 붙었나 — 로그용(«우측 x1600~1980» · «좌측 …» · «떠 있음 …»). <c>Dock</c> 읽기는 쪽을 안 알려 주므로 자리로 가린다.</summary>
    internal static string Where(PaletteSet ps)
    {
        try
        {
            var me = RectOf(ps);
            if (me == null) return "자리 모름";
            if (!IsDocked(ps)) return "떠 있음 " + me.Value;
            var frame = FrameRect();
            if (frame == null) return "붙음(쪽 모름) " + me.Value;
            return (Cx(me.Value) > Cx(frame.Value) ? "우측 " : "좌측 ") + me.Value;
        }
        catch (System.Exception ex) { return "자리 못 읽음(" + ex.Message + ")"; }
    }

    /// <summary>오른쪽에 붙었나(<see cref="Where"/>와 같은 판정) — 각 창의 «창 열기» 줄 ⚠ 표시용.</summary>
    internal static bool IsRight(PaletteSet ps)
    {
        try
        {
            var me = RectOf(ps); var frame = FrameRect();
            return IsDocked(ps) && me != null && frame != null && Cx(me.Value) > Cx(frame.Value);
        }
        catch { return false; }
    }

    static void Register(PaletteSet ps, string name)
    {
        foreach (var e in _all) if (ReferenceEquals(e.Ps, ps)) return;
        _all.Add((ps, name));
    }

    /// <summary>오른쪽에 붙어 보이는 다른 DH 창.</summary>
    static List<(string Name, Box R)> Neighbours(PaletteSet self, Box? frame)
    {
        var list = new List<(string, Box)>();
        foreach (var (ps, name) in _all)
        {
            if (ReferenceEquals(ps, self)) continue;
            try
            {
                if (!ps.Visible || !IsDocked(ps)) continue;
                var r = RectOf(ps);
                if (r == null || r.Value.W <= 0 || r.Value.H <= 0) continue;
                if (frame != null && Cx(r.Value) <= Cx(frame.Value)) continue;    // 왼쪽에 붙인 창은 안 셈
                list.Add((name, r.Value));
            }
            catch { }
        }
        return list;
    }

    /// <summary>나와 가로로 20px 넘게 · 세로로 20px 넘게 겹치는 이웃 이름(없으면 null).</summary>
    static string? HitName(Box me, List<(string Name, Box R)> others)
    {
        foreach (var o in others)
        {
            int ox = Math.Min(me.R, o.R.R) - Math.Max(me.L, o.R.L), oy = Math.Min(me.B, o.R.B) - Math.Max(me.T, o.R.T);
            if (ox > 20 && oy > 20) return o.Name;
        }
        return null;
    }

    /// <summary>★자리 잡은 뒤 한 번 더 — <c>Dock</c> 직후는 AutoCAD가 칸 배치를 아직 안 다시 했을 수 있다(MFC는 배치를 미룬다). 처음 한가해질 때 내 자리와 겹침을 남긴다.</summary>
    static void LogSettled(PaletteSet ps, string name)
    {
        EventHandler? h = null;
        h = (_, _) =>
        {
            AcadApp.Idle -= h;
            try
            {
                var me = RectOf(ps);
                var frame = FrameRect();
                var hit = me == null ? null : HitName(me.Value, Neighbours(ps, frame));
                DiagLog.Append($"\n[도킹] «{name}» 자리 잡은 뒤 — {Where(ps)}"
                             + (hit == null ? " · 겹침 없음" : $" ⚠<b>«{hit}»와 겹침</b>"));
                // ★[v111.9 · 설치 전 검토 M3] 단위: acmgd FloatControl(Rect)는 값을 그대로 int로 잘라 MFC CRect(화면 px)에 넘긴다(ILSpy 1103 — DIP 바꿈 없음) · GetWindowRect도 같은 실의 px라 단위는 맞다.
                //   그래도 자리 잡은 뒤 떠 있거나 · AutoCAD 창 밖이거나 · 이웃과 겹치면 한 번만 그냥 우측(종전 길)으로.
                bool floating = !IsDocked(ps);
                bool off = me != null && frame != null && (me.Value.R <= frame.Value.L || me.Value.L >= frame.Value.R || me.Value.B <= frame.Value.T || me.Value.T >= frame.Value.B);
                if (floating || off || hit != null)
                {
                    try { ps.Dock = DockSides.Right; } catch { }
                    DiagLog.Append($"\n[도킹] «{name}» 되돌림 — {(floating ? "떠 있음" : off ? "창 밖" : $"«{hit}»와 겹침")} → 그냥 우측 · 지금 {Where(ps)}");
                }
            }
            catch { }
        };
        try { AcadApp.Idle += h; } catch { }
    }

    static bool IsDocked(PaletteSet ps) => ps.Dock != DockSides.None;    // 떠 있을 때만 None(①)

    static int Cx(Box b) => (b.L + b.R) / 2;

    static Box? RectOf(PaletteSet ps)
    {
        var h = ps.Handle;
        return h != IntPtr.Zero && GetWindowRect(h, out var r) ? new Box(r.L, r.T, r.R, r.B) : null;
    }

    static Box? FrameRect()
    {
        try
        {
            var h = AcadApp.MainWindow.Handle;
            return h != IntPtr.Zero && GetWindowRect(h, out var r) ? new Box(r.L, r.T, r.R, r.B) : null;
        }
        catch { return null; }
    }

    /// <summary>AutoCAD 창의 오른쪽 도킹 띠(보일 때만) — 못 찾으면 null(로그에 «못 찾음»).</summary>
    static Box? DockBarRect()
    {
        try
        {
            var bar = GetDlgItem(AcadApp.MainWindow.Handle, DockBarRight);
            return bar != IntPtr.Zero && IsWindowVisible(bar) && GetWindowRect(bar, out var r) ? new Box(r.L, r.T, r.R, r.B) : null;
        }
        catch { return null; }
    }

    readonly record struct Box(int L, int T, int R, int B)
    {
        public int W => R - L;
        public int H => B - T;
        public override string ToString() => $"x{L}~{R} y{T}~{B}";
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr hDlg, int id);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
}
