using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using DH.Grading.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901 "그냥 civil3d상의 도킹바를 만들고 그 안에서 웹맵을 열 순 없어?"]
///
/// <para><b>[서버 지표면] 전용 도킹바</b>다 — 지층 구성 도킹바와는 완전히 별개다(JACK 확정).
/// 지표면을 다 가져오면 <b>스스로 닫힌다</b>. 지도를 봐야 하니 <b>넉넉한 크기</b>로 연다.</para>
///
/// <para>★★<b>여기서는 로컬 서버가 없다.</b> 브라우저로 띄우던 때는 포트를 열고 한 번 쓰는 표를
/// 붙여야 했는데(남의 탭이 밀어 넣을 수 있어서), 도킹바 안에서는 <b>창틀에 바로 건넨다</b>
/// (<c>chrome.webview.postMessage</c>). 포트도 표도 프록시도 걸릴 것이 없다.</para>
///
/// <para>★<b>WebView2 판을 Civil 3D와 맞췄다</b>(1.0.2478.35 — Dynamo가 쓰는 것과 같은 판).
/// 다른 판을 넣으면 Dynamo를 켠 뒤 우리 것이 안 뜨거나 그 반대가 된다.
/// 엔진이 아예 없는 PC에서는 <b>예전 브라우저 방식으로 물러난다</b>.</para></summary>
public static class MapPalette
{
    private static PaletteSet _ps;
    private static MapPanel _panel;

    /// <summary>지도가 넘겨준 범위 — <c>DHCONTOURBOX</c>가 받아 쓴다.
    /// <para>★팔레트 단추에서는 도면을 못 고친다(명령 문맥이 아니다). 그래서 여기 놔두고
    /// 명령을 한 줄 태워 <b>명령 문맥 안에서</b> 가져오게 한다 — 이 저장소가 쓰는 방식이다.</para></summary>
    internal static (string DocName, int Epsg, string CsNote,
                     double X0, double Y0, double X1, double Y1, bool Cad)? Pending;

    /// <summary>지도 도킹바를 연다.</summary>
    /// <param name="epsg">조회에 쓸 원점 — <b>부르는 쪽이 정해서 넘긴다</b>(박스와 조회가 같아야 한다).</param>
    internal static void Show(Document doc, int epsg, string csNote)
    {
        try
        {
            if (_ps == null)
            {
                _panel = new MapPanel();
                // ★GUID를 주지 않는다 — 주면 AutoCAD가 상태를 저장했다가 다음 시작에 되살린다.
                _ps = new PaletteSet("서버 지표면 — 지도에서 범위 고르기")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton,
                    DockEnabled = DockSides.Left | DockSides.Right,
                    MinimumSize = new System.Drawing.Size(520, 420),
                };
                _ps.AddVisual("지도", _panel);
            }

            // ★★[JACK 0901 "지도가 넉넉히 보이도록 좀 도킹바가 크게 열려야 해"]
            //   숫자를 박지 않고 <b>화면에서 잰다</b> — 노트북과 27인치에서 같은 느낌이 나야 한다.
            var (w, h) = RoomySize();
            try { _ps.Size = new System.Drawing.Size(w, h); } catch { }

            // ★도킹은 <b>보인 뒤에</b> 건다 — 안 뜬 팔레트에는 AutoCAD가 Dock을 무시한다(§56에서 데였다).
            _ps.Visible = true;
            try { if (_ps.Dock != DockSides.Right) _ps.Dock = DockSides.Right; } catch { }
            try { _ps.Size = new System.Drawing.Size(w, h); } catch { }

            _panel.Start(doc, epsg, csNote);

            try
            {
                DiagLog.Append($"\n[지도도킹바] 열기 — 요청 {w}×{h}px · 실제 {_ps.Size.Width}×{_ps.Size.Height}px"
                             + $" · 도킹 {_ps.Dock} · EPSG:{epsg}");
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            doc?.Editor.WriteMessage("\n[지도도킹바] 열지 못했습니다 — " + ex.Message);
        }
    }

    /// <summary>가져온 자리로 화면을 옮긴다.
    /// <para>★★★[JACK 0901 "그 위치로 확대 포커싱이 안 돼" / "줌-범위 하면 될 것 같은데"]</para>
    /// <para><b>ZOOM 명령을 쓴다.</b> <c>SetCurrentView</c>는 예외도 안 던지고 조용히 안 먹는
    /// 자리가 있어서 "옮겼다"고 적어 놓고 화면은 그대로인 일이 생긴다.</para>
    /// <para>먼저 <b>가져온 범위로 창(W)</b>을 맞춘다 — 기존 도면에 다른 것이 멀리 있으면
    /// 범위(E)는 엉뚱하게 넓어지기 때문이다. 그게 실패하면 JACK 말대로 <b>범위(E)</b>로 간다.</para></summary>
    private static void ZoomTo(Document doc, double x0, double y0, double x1, double y1)
    {
        var ed = doc.Editor;
        double w = Math.Max(1.0, x1 - x0), h = Math.Max(1.0, y1 - y0);
        double px = w * 0.05, py = h * 0.05;      // 가장자리가 붙지 않게 조금 넓혀서
        string how = "창(W)";
        try
        {
            ed.Command("_.ZOOM", "_W",
                new Autodesk.AutoCAD.Geometry.Point3d(x0 - px, y0 - py, 0),
                new Autodesk.AutoCAD.Geometry.Point3d(x1 + px, y1 + py, 0));
        }
        catch (System.Exception ex)
        {
            how = "창 실패(" + ex.Message + ") → 범위(E)";
            try { ed.Command("_.ZOOM", "_E"); }
            catch (System.Exception ex2) { how += " 도 실패(" + ex2.Message + ")"; }
        }

        // ★쓴 뒤에 <b>실제 화면</b>을 재 본다 — 못 갔으면 찾는 법을 알려 준다(§53).
        double off = double.NaN;
        try
        {
            using var got = ed.GetCurrentView();
            off = Math.Max(Math.Abs(got.CenterPoint.X - (x0 + x1) / 2),
                           Math.Abs(got.CenterPoint.Y - (y0 + y1) / 2));
        }
        catch { }
        bool ok = !double.IsNaN(off) && off <= Math.Max(w, h);
        ed.WriteMessage(ok
            ? $"\n[서버 지표면] 화면 이동 — {w:F0}×{h:F0}m"
            : "\n[서버 지표면] 화면 이동 실패 — ZOOM E(범위)로 찾으세요.");
        try { DiagLog.Append($"\n[지도도킹바] 화면 이동 {how} · 중심 어긋남 {off:F1}m · {(ok ? "성공" : "실패")}"); } catch { }
    }

    /// <summary>화면 크기에 맞춘 <b>넉넉한</b> 폭·높이(px).
    /// <para>지도는 폭이 좁으면 아무것도 못 고른다. 화면의 45%쯤을 쓰되
    /// 너무 작지도 너무 크지도 않게 가둔다.</para></summary>
    private static (int W, int H) RoomySize()
    {
        double sw = 1920, sh = 1080;
        try
        {
            sw = SystemParameters.PrimaryScreenWidth;
            sh = SystemParameters.PrimaryScreenHeight;
        }
        catch { }
        int w = (int)Math.Round(Math.Min(980, Math.Max(620, sw * 0.45)));
        int h = (int)Math.Round(Math.Min(1000, Math.Max(520, sh * 0.72)));
        return (w, h);
    }

    /// <summary>★[JACK 0901 "지표면 부르기가 완료되면 도킹바도 닫혀야 해"]
    /// <para>★★<b>닫으면 넘긴 범위도 같이 버린다</b>(검토 0901). 명령은 <b>줄을 서서</b>
    /// 실행되므로, 보내고 나서 마음이 바뀌어 [그만두기]를 눌러도 그 사이에 큐에 든 명령이
    /// 그대로 돌아 도면을 고쳐 버렸다 — 창은 닫혔는데 지형이 들어온다.</para></summary>
    internal static void Close()
    {
        Pending = null;
        try { if (_ps != null) _ps.Visible = false; } catch { }
    }

    /// <summary>지도에서 고른 범위로 <b>실제로 가져오는</b> 자리 — 명령 문맥 안이라 도면을 고칠 수 있다.
    /// <para>사람이 직접 칠 명령이 아니다(팔레트가 태운다).</para></summary>
    [CommandMethod("DHCONTOURBOX", CommandFlags.Modal)]
    public static void RunPending()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var got = Pending;
        Pending = null;                       // ★한 번만 쓴다 — 남겨 두면 다음에 또 가져온다
        if (got == null)
        {
            doc.Editor.WriteMessage("\n[서버 지표면] 넘겨받은 범위가 없습니다.");
            return;
        }
        var p = got.Value;
        try
        {
            // ★★<b>고른 도면과 넣는 도면이 같은지</b> 본다(검토 0901). 팔레트는 도면보다 오래 살아서
            //   지도를 띄워 놓고 다른 도면으로 넘어갈 수 있다. 원점이 다르면 <b>178km 옆</b>이 들어온다.
            if (!string.Equals(doc.Name, p.DocName, StringComparison.OrdinalIgnoreCase))
            {
                doc.Editor.WriteMessage(
                    $"\n[서버 지표면] 범위를 고른 도면과 지금 도면이 다릅니다 — 가져오지 않았습니다."
                  + $"\n  고른 곳: {p.DocName}\n  지금: {doc.Name}");
                return;
            }
            // ★크기 관문은 <b>넣는 쪽</b>에서 본다 — 그래야 범위를 주는 길이 늘어도 구멍이 안 생긴다(§50).
            if (!ImportGisCommand.Sane(doc.Editor, "서버 지표면", p.X0, p.Y0, p.X1, p.Y1)) return;
            bool ok = ImportGisCommand.ImportContourBox(doc, p.Epsg, p.CsNote, p.X0, p.Y0, p.X1, p.Y1);
            // ★[JACK 0901 "지적도 체크하면 지적도랑 지표면 같이 불러지게 해"]
            //   <b>같은 범위</b>로 이어서 받는다 — 따로 두 번 고르게 하지 않는다.
            //   다만 지표면이 실패했으면 건너뛴다 — 오류 대화상자가 둘 연달아 뜬다.
            if (p.Cad && ok)
                ImportGisCommand.ImportParcelBox(doc, p.Epsg, p.CsNote, p.X0, p.Y0, p.X1, p.Y1, alone: false);
            else if (p.Cad)
                doc.Editor.WriteMessage("\n[지적도] 지표면을 못 가져와 지적도는 건너뜁니다.");
        }
        finally
        {
            Close();                          // 다 됐으면 도킹바를 닫는다(JACK 지시)
            // ★[JACK 0901 "도킹바 닫히면 가져온 지표면으로 자동 화면 전환해 줘"]
            //   빈 도면은 화면이 원점(0,0) 근처인데 현장은 20만 미터 밖이다 —
            //   안 옮기면 "가져왔다는데 아무것도 없다"가 된다.
            ZoomTo(doc, p.X0, p.Y0, p.X1, p.Y1);
        }
    }
}

/// <summary>도킹바 알맹이 — <b>지도 한 장</b>과, 엔진이 없을 때의 <b>안내</b>.</summary>
internal sealed class MapPanel : UserControl
{
    private readonly Grid _root = new();

    /// <summary>★★★[JACK 0901 "좌표계에 대해서 문외한 사람들이 많이 사용할 거기 때문에
    /// 최대한 간략하고 쉽게 만들어야 해. <b>좌표계가 두 가지가 존재한다고 생각이 안 들게끔</b>"]
    ///
    /// <para><b>원인은 우리 쪽에 있었다.</b> 이 애드인은 원점을 두 곳에서 본다 —
    /// 도면에 박힌 좌표계, 그리고 정지설정. 둘이 다르면 어느 쪽이 이겼는지 적어 주는 게
    /// 정직하다고 생각했는데, <b>그 정직함이 곧 "두 개가 있다"는 고백</b>이었다.</para>
    ///
    /// <para>→ <b>둘을 갈라 두지 말고 붙인다.</b> 여기서 고르면 도면 좌표계와 정지설정이
    /// <b>같이</b> 바뀐다. 그러면 볼 것도 고를 것도 하나뿐이라 다를 수가 없다.</para>
    ///
    /// <para>이름도 "EPSG:5186"이 아니라 <b>"중부원점 — 서울·경기·충청·전라·제주"</b>다.
    /// 숫자는 아는 사람만 필요하므로 <b>말풍선</b>으로 보낸다.</para></summary>
    private readonly ComboBox _csPick = new()
    {
        Margin = new Thickness(6, 0, 0, 0),
        MinWidth = 240,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _csHint = new()
    {
        FontSize = 11,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };
    private bool _csFilling;


    private readonly TextBlock _msg = new()
    {
        Margin = new Thickness(18),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Text = "지도 준비 중…",
    };
    private readonly StackPanel _fallback = new()
    {
        Margin = new Thickness(18),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };
    private WebView2 _web;
    private Document _doc;
    private int _epsg;
    private string _csNote = "";
    private bool _starting;
    private bool _ready;
    private bool _loaded;
    private System.Windows.Threading.DispatcherTimer _watchdog;

    /// <summary>지도 페이지를 놓아 둘 자리 — <b>쓸 수 있는 곳</b>이어야 한다.
    /// <para>WebView2는 기본으로 실행 파일 옆에 작업 폴더를 만들려 하는데
    /// 그건 <c>C:\Program Files\...\acad.exe</c> 옆이라 못 쓴다 — 그러면 아예 안 뜬다.</para></summary>
    private static string HomeDir =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DH.Grading", "map");

    internal MapPanel()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock
        {
            Text = "정지옵션 좌표계",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        });
        line.Children.Add(_csPick);

        var stack = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
        stack.Children.Add(line);
        stack.Children.Add(_csHint);

        var head = new Border
        {
            Background = System.Windows.Media.Brushes.WhiteSmoke,
            BorderBrush = System.Windows.Media.Brushes.Gainsboro,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack,
        };
        _csPick.SelectionChanged += OnCsChanged;
        Grid.SetRow(head, 0);
        _root.Children.Add(head);
        Grid.SetRow(_msg, 1);
        Grid.SetRow(_fallback, 1);
        _root.Children.Add(_msg);
        _root.Children.Add(_fallback);
        Content = _root;
    }

    /// <summary>도킹바가 열릴 때마다 부른다 — 지도를 (필요하면) 만들고 새로 그린다.</summary>
    internal void Start(Document doc, int epsg, string csNote)
    {
        _doc = doc; _epsg = epsg; _csNote = csNote;
        ShowCs();
        _shownFor = null;      // 열 때마다 새 지도 — 지난번 사각형·문구가 남아 있으면 헷갈린다
        if (_ready) { Navigate(); return; }
        if (_starting) return;
        _starting = true;
        _ = InitAsync();
    }

    /// <summary>고르는 칸을 채운다 — <b>지금 쓰는 것 하나</b>만 골라져 있다.</summary>
    private void ShowCs()
    {
        try
        {
            _csFilling = true;
            _csPick.Items.Clear();
            // ★이름은 정지설정 목록을 <b>그대로</b> 쓴다 — 두 화면이 다른 말을 하면 안 된다(§50).
            int sel = -1;
            for (int i = 0; i < GradingDialog.EpsgCodes.Length; i++)
            {
                _csPick.Items.Add(new ComboBoxItem
                {
                    Content = GradingDialog.CoordLabels[i],
                    Tag = GradingDialog.EpsgCodes[i],
                });
                if (GradingDialog.EpsgCodes[i] == _epsg) sel = i;
            }
            if (sel < 0)
            {
                _csPick.Items.Add(new ComboBoxItem { Content = $"EPSG:{_epsg}", Tag = _epsg });
                sel = _csPick.Items.Count - 1;
            }
            _csPick.SelectedIndex = sel;
            _csHint.Text = "변경 시 정지옵션에 저장";
            _csHint.Foreground = System.Windows.Media.Brushes.DimGray;
        }
        catch { }
        finally { _csFilling = false; }
    }

    /// <summary>★고르면 <b>도면 좌표계와 정지설정이 같이</b> 바뀐다 —
    /// 그래야 "두 개가 따로 논다"는 일이 아예 안 생긴다(JACK 0901).</summary>
    private void OnCsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_csFilling) return;
        try
        {
            if (_csPick.SelectedItem is not ComboBoxItem it || it.Tag is not int epsg) return;
            if (epsg == _epsg) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            GradingSettings.ExportEpsg = epsg;                     // 정지설정
            var (ok, note) = KoreaCs.Assign(doc.Database, epsg);   // 도면 좌표계
            _epsg = epsg;
            _csNote = note;
            _csHint.Text = ok
                ? "정지옵션 저장됨 · 도면 좌표계 적용됨"
                : "정지옵션 저장됨 · 도면 좌표계 적용 실패 — " + note;
            _csHint.Foreground = ok
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.SaddleBrown;
            try { doc.Editor.WriteMessage("\n[서버 지표면] 좌표 기준 변경 — " + note); } catch { }
            try { DiagLog.Append($"\n[지도도킹바] 좌표 기준 바꿈 → EPSG:{epsg} · {note}"); } catch { }
        }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n[지도도킹바] 좌표 기준 바꾸기 실패 — " + ex.Message); } catch { }
        }
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        try
        {
            System.IO.Directory.CreateDirectory(HomeDir);
            var env = await CoreWebView2Environment.CreateAsync(null, HomeDir);

            // ★★★<b>붙이고 나서 띄운다 — 순서를 바꾸면 안 된다</b>(JACK 0901 "아까는 도킹바일 때도 나왔었어").
            //   <b>원인이 여기였다.</b> 검토가 "실패하면 반쯤 만들어진 것이 남는다"고 해서
            //   <b>띄운 다음 붙이도록</b> 순서를 바꿨는데, WebView2는 <b>창 안에 창을 다는</b> 물건이라
            //   붙을 창이 없는 상태로 띄우면 그릴 곳을 못 찾아 <b>검은 사각형</b>만 남는다.
            //   쌓이는 문제는 순서가 아니라 <b>실패했을 때 치우는 것</b>으로 푼다(아래 catch).
            var web = new WebView2();
            _web = web;
            try { _web.DefaultBackgroundColor = System.Drawing.Color.White; } catch { }
            Grid.SetRow(_web, 1);
            _root.Children.Insert(0, _web);
            try { await _web.EnsureCoreWebView2Async(env); }
            catch
            {
                // 실패하면 <b>여기서 치운다</b> — 이러면 다음에 불러도 안 쌓인다(검토 0901 HIGH 해결).
                try { _root.Children.Remove(web); } catch { }
                try { web.Dispose(); } catch { }
                _web = null;
                throw;
            }

            var core = _web.CoreWebView2;
            // 필요 없는 것은 꺼 둔다 — 도면 옆에 뜨는 창이라 조용해야 한다.
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            // ★파일을 가짜 도메인에 걸어 준다 — 이러면 페이지가 <b>진짜 https 출처</b>를 갖는다.
            //   문자열로 직접 띄우면 출처가 없어 타일·스크립트가 막히는 판이 있다.
            core.SetVirtualHostNameToFolderMapping(
                "dh.map", HomeDir, CoreWebView2HostResourceAccessKind.DenyCors);

            core.WebMessageReceived += OnMessage;
            // ★★페이지가 떴는지 <b>기록으로 남긴다</b> — 검은 화면이 또 나오면
            //   "안 열린 것"인지 "열렸는데 타일이 없는 것"인지 이 한 줄로 갈린다.
            core.NavigationCompleted += (_, e2) =>
            {
                _loaded = e2.IsSuccess;
                try
                {
                    DiagLog.Append($"\n[지도도킹바] 페이지 {(e2.IsSuccess ? "떴다" : "실패 " + e2.WebErrorStatus)}"
                                 + $" · 칸 {ActualWidth:F0}×{ActualHeight:F0}px");
                }
                catch { }
            };
            // ★엔진 프로세스가 죽으면 다음 Navigate가 영영 터진다 — 그때는 처음부터 다시 하게 둔다.
            core.ProcessFailed += (_, __) => ShowFallback("지도 엔진이 멈췄습니다");
            // ★우리 페이지 밖으로는 안 간다 — 링크 한 번에 엉뚱한 사이트가 CAD 안에 뜨면 안 된다.
            core.NavigationStarting += (_, e) =>
            {
                // ★<c>about:blank</c>는 WebView2가 스스로 여는 첫 페이지다 — 이것까지 막으면 안 된다.
                string u = e.Uri ?? "";
                if (u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
                if (!u.StartsWith("https://dh.map/", StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; };   // 새 창은 안 띄운다

            _msg.Visibility = Visibility.Collapsed;
            _ready = true;
            Navigate();
        }
        catch (System.Exception ex)
        {
            _starting = false;
            ShowFallback(ex.Message);
        }
    }

    /// <summary>페이지를 연다 — 단, <b>칸에 크기가 생긴 다음</b>에.
    /// <para>★★★[JACK 0901 "지도가 안 나와 검은 화면이야"]</para>
    /// <para><b>원인: 크기가 0일 때 페이지를 열었다.</b> 팔레트를 띄우자마자 여기로 오는데
    /// 그 시점에는 WPF가 아직 칸 크기를 계산하지 않았다. Leaflet은 만들어질 때의 창 크기로
    /// 깔 타일을 정하므로 0×0이면 <b>한 장도 안 깐다</b> — 배경색(검정)만 남는다.
    /// 브라우저로 띄우던 때는 창이 처음부터 크기가 있어 안 났던 자리다.</para>
    /// <para>→ 크기가 생길 때까지 <b>기다렸다가</b> 연다. 페이지 쪽에서도 창이 바뀔 때마다
    /// 다시 재게 해 두었다(둘 중 하나만으로는 도킹바를 끌어 넓힐 때 또 빈다).</para></summary>
    private void Navigate()
    {
        // ★★<b>기다리지 않고 바로 연다</b>(0901). 크기가 생길 때까지 기다리게 했더니
        //   그 조건이 안 풀려 <b>지도를 아예 안 열었다</b> — 검은 화면의 두 번째 원인은 그것이었다.
        //   크기 문제는 페이지 쪽에서 스스로 다시 재는 것으로 푼다(invalidateSize).
        NavigateNow();
    }

    private string _shownFor;

    private void NavigateNow()
    {
        try
        {
            var belt = ShapefileWriter.Belt(_epsg);
            double cm = belt?.cm ?? 127;
            string html = MapPage.Build(cm, "", embedded: true);
            System.IO.Directory.CreateDirectory(HomeDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(HomeDir, "index.html"), html,
                                        new System.Text.UTF8Encoding(false));
            // 같은 원점으로 이미 띄워 놨으면 다시 안 띄운다 — 고른 박스가 지워진다.
            string key = _epsg.ToString();
            if (_shownFor == key) return;
            _shownFor = key;
            _loaded = false;
            _web.CoreWebView2.Navigate("https://dh.map/index.html");
            // ★<b>검은 화면에 가두지 않는다</b> — 8초 안에 안 뜨면 나갈 길을 보여 준다.
            try { _watchdog?.Stop(); } catch { }
            _watchdog = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8),
            };
            _watchdog.Tick += (_, __) =>
            {
                try { _watchdog.Stop(); } catch { }
                if (!_loaded) ShowFallback("지도 페이지가 8초 안에 뜨지 않았습니다");
            };
            _watchdog.Start();
            try { DiagLog.Append($"\n[지도도킹바] 페이지 열기 — 칸 {_web.ActualWidth:F0}×{_web.ActualHeight:F0}px · EPSG:{_epsg}"); } catch { }
        }
        catch (System.Exception ex) { ShowFallback(ex.Message); }
    }

    /// <summary>엔진이 없거나 못 떴다 — <b>막다른 길로 두지 않는다</b>.
    /// <para>왜 안 되는지 적고, 예전 브라우저 방식으로 갈 단추를 준다.</para></summary>
    private void ShowFallback(string why)
    {
        try
        {
            // ★★<b>웹뷰를 먼저 치운다</b>(검토 0901). WebView2는 창 안에 창을 띄우는 물건이라
            //   WPF 순서와 상관없이 <b>항상 위를 덮는다</b> — 안 치우면 안내도 단추도 그 뒤에 가려
            //   사용자는 빈 지도만 보고 나갈 길을 못 찾는다.
            try { _watchdog?.Stop(); } catch { }
            try { if (_web != null) { _root.Children.Remove(_web); _web.Dispose(); } } catch { }
            _web = null;
            _ready = false; _starting = false;   // 다음에 부르면 처음부터 다시 해 본다

            _msg.Visibility = Visibility.Collapsed;
            _fallback.Children.Clear();
            _fallback.Children.Add(new TextBlock
            {
                Text = "지도 표시 실패\n\n"
                     + "WebView2 엔진 없음 또는 차단됨.\n"
                     + "아래 단추로 브라우저에서 여세요.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });
            var b = new Button
            {
                Content = "브라우저로 지도 열기",
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            };
            b.Click += (_, __) =>
            {
                MapPalette.Close();
                _doc?.SendStringToExecute("DHCONTOURWEB ", true, false, true);
            };
            _fallback.Children.Add(b);
            _fallback.Children.Add(new TextBlock
            {
                Text = "사유: " + why,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 11,
            });
            _fallback.Visibility = Visibility.Visible;
            try { DiagLog.Append("\n[지도도킹바] WebView2를 못 띄웠다 — " + why); } catch { }
        }
        catch { }
    }

    /// <summary>지도가 건넨 말 — <c>{kind:'box'|'cancel', box:{...}}</c>.</summary>
    private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // ★터지면 AutoCAD가 통째로 죽는 자리다(WPF 이벤트 안이라 받아 줄 사람이 없다).
        try { OnMessageCore(e); }
        catch (System.Exception ex)
        {
            try { DiagLog.Append("\n[지도도킹바] 범위 처리 중 오류 — " + ex.Message); } catch { }
            Reject("범위를 처리하지 못했습니다: " + ex.Message);
        }
    }

    /// <summary>지도에 <b>안 됐다고 알린다</b> — 안 그러면 "받는 중"에서 영영 멈춘 것처럼 보인다.</summary>
    private void Reject(string why)
    {
        try { _web?.CoreWebView2?.PostWebMessageAsString("reject:" + why); } catch { }
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[서버 지표면] " + why); } catch { }
    }

    private void OnMessageCore(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch { try { raw = e.WebMessageAsJson; } catch { return; } }
        if (string.IsNullOrEmpty(raw)) return;

        // 지도가 제 상태를 알려 온 것 — 검은 화면일 때 왜인지 로그에 남는다.
        if (raw.IndexOf("\"diag\"", StringComparison.Ordinal) >= 0)
        {
            try { DiagLog.Append("\n[지도도킹바] " + raw); } catch { }
            return;
        }
        if (raw.IndexOf("\"cancel\"", StringComparison.Ordinal) >= 0)
        {
            try { _doc?.Editor.WriteMessage("\n[서버 지표면] 지도를 그만두었습니다."); } catch { }
            MapPalette.Close();
            return;
        }

        var box = MapPickCommand.BoxServer.Parse(raw);   // ★읽는 규칙은 한 벌뿐이다(§50)
        if (box == null) { Reject("지도가 보낸 값을 읽지 못했습니다."); return; }

        // ★★<b>도면은 쓸 때 찾는다</b>(검토 0901). 팔레트는 도면보다 오래 살아서
        //   열어 둔 채 도면을 닫으면 붙잡아 둔 것은 <b>죽은 도면</b>이다 — 건드리면 AutoCAD가 죽는다.
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) { Reject("열려 있는 도면이 없습니다."); return; }
        var ed = doc.Editor;
        // 쓰는 값은 <b>정지옵션 하나</b>다(JACK 0901) — 화면에 보이는 것과 같아야 하기 때문이다.
        int epsg = GradingSettings.ExportEpsg;
        string csNote = _csNote;
        if (epsg != _epsg)
        {
            _epsg = epsg; _doc = doc;
            ShowCs();
            ed.WriteMessage($"\n[서버 지표면] 정지옵션 좌표계가 바뀌어 다시 잡았습니다 — EPSG:{epsg}");
        }
        if (!MapPickCommand.ToTm(ed, epsg, box, out double x0, out double y0, out double x1, out double y1))
        {
            Reject($"좌표계 EPSG:{epsg}로는 옮기지 못했습니다.");
            return;
        }
        if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
        {
            Reject($"고른 범위가 너무 작습니다(가로 {x1 - x0:F1}m × 세로 {y1 - y0:F1}m) — 다시 골라 주세요.");
            return;
        }

        // ★도면을 고치는 일은 <b>명령 문맥</b>에서 한다 — 팔레트 단추에서 바로 하면 안 되는 자리다.
        // 지도가 실은 체크 상태 — 따옴표를 세지 않고 <b>키 이름</b>으로만 본다(공백에도 안 깨지게).
        bool alsoCad = System.Text.RegularExpressions.Regex.IsMatch(
            raw, @"""cad""\s*:\s*true");
        MapPalette.Pending = (doc.Name, epsg, csNote, x0, y0, x1, y1, alsoCad);
        doc.SendStringToExecute("DHCONTOURBOX ", true, false, true);
    }
}
