using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents; // TextElement
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 겹침 검사의 결과 창 — 글만 든 알림 창 대신, 숫자 칸 · 쌍 목록 · 단추로 보여 준다
/// (JACK 2026-10-13: "메시지상자말고 팝업의 기능으로 … 좀 직관적이게").
/// JACK 2026-10-14(Revit 에서 써 본 뒤): "겹침도 위치정하기처럼 팝업에서 겹쳐진 부위만 … 보여줬으면 좋겠어. 아닌 부분은 외곽선만 나오게 투명하게"
///  → 겹친 쌍이 있으면(찾은 뒤 · 결합한 뒤) 넓은 창: 왼쪽 = 쌍 목록(남는 쪽 → 잘리는 쪽의 묶음), 오른쪽 = 「위치·부위 정하기」와 같은 3D 그림.
///    줄을 누르면 창은 그대로 있고 그림이 그 자리로 간다. 그림의 덩어리 · 색칠된 부재를 눌러도 그 쌍으로 간다.
/// JACK 2026-10-14(두 번째): "겹친 부재(노란색), 겹쳐진 부위(빨간색), 나머지는 투명" · "목록에서 눌렀을 때 그 객체만 수정되는 버튼" ·
///    "기본 모드 … 우선순위로 · 사용자 모드 … 어느 쪽으로 삭제시킬지" · "아래 파란색으로 설명 있는 건 다 지워" · 목록과 3D 사이를 마우스로 끌어 폭 조절.
///  → 전체 보기 = 겹친 부위만 빨강(부재는 모두 외곽선만) · 쌍을 고르면 그 두 부재(남는 쪽 주황 · 잘리는 쪽 초록 — 처음의 노랑을 JACK 이 1014 16시에 바꿈),
///    그 쌍의 부위 = 빨강, 나머지 = 외곽선만. 고친 부위는 남는 쪽의 색(주황)이 된다.
///    그림 위 띠에 [기본 | 사용자] + 고치기 단추: 기본 = 우선순위대로 · 사용자 = 어느 부재에서 겹친 만큼을 뺄지 고른다.
///    고친 뒤에도 창은 그대로 — 그 줄은 결과 딱지, 부위는 초록, 그림은 고친 모양으로 다시 그린다.
///    창 안에는 제목 · 숫자 · 목록 · 단추 · 짧은 이름표만 둔다(풀이는 풍선 글로).
///  · 겹침 없음(Clean) · 대상 없음(NoTargets)은 작은 창 그대로다.
/// 상태와 규칙(묶음의 차례 · 초점 · 누가 무슨 색인지 · 찍으면 어느 쌍인지)은 Core 의 OverlapBoard 가 들고 있고, 여기서는 그리기와 전하기만 한다.
/// Revit API 를 쓰지 않는다 — 화면만 따로 그려 그림 파일로 보고, 진짜 창에 마우스 신호를 넣어 눌러 볼 수 있게(tools/uipreview).
/// 모델을 바꾸는 일은 부르는 쪽이 넣어 준 Model.Fix 로만 하고(창에서 고치기), 그 밖에는 누른 것(Choice)만 돌려준다 — 부르는 쪽(CheckOverlapCommand)이 창이 닫힌 뒤에 한다.
/// </summary>
public sealed class OverlapResultView
{
    public enum Mode { Found, Joined, Clean, NoTargets }

    /// <summary>창에서 누른 것. Pair = [Revit 에서 보기](그 두 부재를 보러 간다) · CheckAll = [전체 다시 검사].</summary>
    public enum Choice { Close, Join, SelectAll, SelectProblems, Pair, CheckAll }

    public sealed class Model
    {
        public Mode Mode;
        public int Checked;                 // 검사한 부재 수
        public bool SelectionOnly;          // 선택한 부재만 검사했다
        public bool CanCheckAll;            // [전체 다시 검사]를 둔다(선택한 부재만 검사했고, 결합한 뒤의 창이 아니다)
        public List<OverlapRow> Rows = new();
        public double TotalM3;              // 찾은 중복 체적
        public int Joined, StillOverlap, Failed, NotNeeded;   // 쌍 전부의 결합 결과를 센 수(창에서 고치면 Fixed 로 새로 받는다)
        public double RemainingM3;          // 남은 중복 체적(아직 안 고친 쌍 포함)
        public List<string> Notes = new();  // 잴 수 없었던 쌍 · 함께 있지 않아 뺀 쌍 같은 덧붙임
        public string LogPath = "";         // 결과표 파일. 쓰지 못했으면 ""
        public string LogProblem = "";      // 결과표를 쓰지 못한 까닭
        public string PriorityText = "";
        public string StaleVerdict = "";    // 결합한 부재의 판정에 적어 둔 글

        // ── 3D ──
        public IReadOnlyList<string> MemberNames = Array.Empty<string>();   // 부재의 이름 — Meshes 와 같은 차례
        public IReadOnlyList<MeshData> Meshes = Array.Empty<MeshData>();    // 부재의 겉면(쌍의 WinnerIndex · LoserIndex 가 이 차례를 가리킨다)
        public IReadOnlyList<MeshData?> Blobs = Array.Empty<MeshData?>();   // 겹친 덩어리 — 쌍의 Key 번째(없으면 null)
        public string SceneProblem = "";    // 3D 를 만들지 못한 까닭("" = 만들었다)

        /// <summary>창 안에서 쌍 하나를 고친다(쌍의 Key, 남는 쪽에서 뺄 것인가) — 부르는 쪽(명령)이 넣는다. null 이면 고치기 단추를 두지 않는다.</summary>
        public Func<int, bool, Fixed>? Fix;

        /// <summary>
        /// 고치기 미리보기: 그 쌍을 고치면 잘릴 부재의 새 겉면과 그 그림 차례(쌍의 Key, 남는 쪽에서 뺄 것인가) — 부르는 쪽이 넣는다.
        /// 못 만들면 (null, −1). 이것이 없거나 못 만들면 창은 그 쌍의 덩어리를 남을 부재의 색으로 칠해 대신 보인다.
        /// </summary>
        public Func<int, bool, (MeshData? mesh, int at)>? Preview;

        /// <summary>이미 결합됐는데 버림이 잘린 쌍의 수 — [우선순위대로 결합]이 함께 순서를 바꾼다(겹친 쌍이 없으면 작은 창의 단추로).</summary>
        public int LeanToFix;

        /// <summary>이미 결합됐는데 나중에 치는 부재(받침 · PAD · 디딤판)가 구조체를 깎은 쌍의 수 — 버림과 같이 순서를 바꾼다(JACK 2026-10-15 «나중 부재가 늘 잘림»).</summary>
        public int LaterToFix;

        /// <summary>이미 결합돼 두 덩이인 벽 · 기둥의 수 — [우선순위대로 결합]이 함께 나눈다(겹친 쌍이 없으면 작은 창의 단추로 · JACK 2026-10-15).</summary>
        public int SplitToDo;
    }

    /// <summary>창에서 쌍 하나를 고친 결과 — 부르는 쪽이 채운다.</summary>
    public sealed class Fixed
    {
        public List<OverlapRow> Rows = new();               // 바뀐 줄: 고친 쌍 + 그 일로 겹침이 없어진 쌍
        public Dictionary<int, MeshData> Meshes = new();    // 모양이 바뀐 부재의 새 겉면(그림의 차례 → 겉면)
        public int Joined, StillOverlap, Failed, NotNeeded; // 쌍 전부를 다시 센 수
        public double RemainingM3;
        public bool Changed;                                // 이번 것으로 모델이 바뀌었다(결합이 커밋됐다)
        public string Problem = "";                         // 알릴 것(못 고친 까닭 · 겹침이 남음). "" = 없음
        public int SplitToDo = -1;                          // 남은 두 덩이 벽 · 기둥의 수(이 쌍에서 나누기를 해 본 부재를 뺀 것 · −1 = 그대로)
        // 고친 뒤 다시 찾기(JACK 2026-10-19 «하나를 결합하더라도 새로고침»):
        public List<OverlapRow> Added = new();              // 새로 나온 쌍의 줄(Key = 찾은 쌍 끝에 붙인 차례 — 목록에서는 제 묶음 맨 위)
        public Dictionary<int, MeshData?> Blobs = new();    // 덩어리가 바뀐 쌍(Key → 새 덩어리 · null = 그리지 않음) — 새 쌍 · 체적이 바뀐 쌍
        public int Gone;                                    // 이번 다시 찾기로 겹침이 없어진 다른 쌍의 수(알림 글)
    }

    // 3D 의 색(JACK 2026-10-14 세 번째: "겹치는 부위는 빨간색, 첫번째 부재는 주황색, 두번째 부재는 녹색 … 고치고 나면 그 부위가 해당하는 부재 색상으로"):
    // 겹친 부위 = 빨강 · 남는 쪽(앞) = 주황 · 잘리는 쪽(뒤) = 초록 · 고친 부위 = 남는 쪽의 색(주황 — 그 자리를 가진 부재) ·
    // 이미 풀린 부위(앞선 결합으로 겹침이 없어짐 — 누구 것인지 모른다) = 회색.
    private static readonly (byte r, byte g, byte b) WinnerColor = (251, 140, 0), LoserColor = (40, 167, 88), BlobColor = (226, 44, 44), LooseColor = (150, 156, 166);
    private const double WideSide = 452;      // 넓은 창의 왼쪽(목록) 처음 너비 — 가르개로 바꾼다
    private const double SideMin = 300, StageMin = 420, SplitterWidth = 12;

    // OverlapChecker.Joined / NotNeeded 와 같은 글자(그쪽은 Revit API 를 쓰는 파일이라 여기서 부르지 않고, 부르는 쪽이 넘긴다).
    private readonly string _joinedText, _notNeededText;
    private readonly UiTheme _ui;
    private readonly Model _m;
    private readonly bool _wide;                             // 넓은 창(목록 + 3D)
    private readonly OverlapBoard _board;
    private readonly Model3DView? _view3d;
    private readonly int _members;                           // 그림에서 부재의 수(그 뒤가 덩어리다)
    private readonly List<int> _blobKey = new();             // 그림의 덩어리 차례 → 쌍의 Key
    private readonly Dictionary<int, int> _blobAt = new();   // 쌍의 Key → 그림의 차례
    private readonly List<MeshData> _drawn = new();          // 그림에 넘긴 겉면(부재 + 덩어리 — 고친 부재는 새 겉면으로 바꿔 둔다)
    private readonly StackPanel _list = new() { Margin = new Thickness(4) };
    private readonly Grid _header = new();                   // 목록 머리(칸 이름) — 결과 칸이 생기면 다시 짓는다
    private readonly Dictionary<int, Button> _rowOf = new();
    private readonly TextBlock _focusHead = new(), _focusSub = new(), _hover = new(), _message = new(), _sideTitle = new();
    private readonly Border _hoverPill = new(), _toast = new();
    private readonly Border _headHost = new(), _buttonsHost = new(), _fixRow = new();
    private readonly StackPanel _fixActions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private Button? _previous, _next, _showAll, _inRevit;
    private RadioButton? _basic, _user;
    private ColumnDefinition? _sideColumn;
    private ScrollViewer? _listScroll;
    private Window? _window;
    private int _preview = -1;                               // 고치기 단추에 마우스를 올린 동안: 겹친 만큼이 빠질 부재(그림의 차례) — 고친 뒤의 모습으로 그린다
    private int _shaped = -1;                                // 그동안 겉면을 고친 뒤의 모양으로 잠깐 바꿔 둔 부재(그림의 차례) — 떼면 _drawn 의 것으로 돌린다
    private readonly Dictionary<(int key, bool cutWinner), (MeshData? mesh, int at)> _cutShapes = new();   // 받아 둔 미리보기 모양(고치면 비운다)
    private int _solo = -1;                                  // 목록에서 부재 이름을 눌렀다: 그 부재만 칠한다(그림의 차례 — JACK 2026-10-19) · 초점이 바뀌면 풀린다
    private readonly List<(int key, int member, bool winner, Button cell)> _cells = new();   // 목록의 부재 이름 칸(누를 수 있는 것만)
    private bool _changedModel;                              // 이 창에서 고친 것이 모델에 걸렸다
    private bool _busy;                                      // 고치는 중(Revit 이 결합하는 동안) — 누르기 · 글쇠 · 닫기를 막는다
    private static bool s_userMode;                          // 고치기의 사용자 모드(Revit 을 끌 때까지 기억한다)
    private readonly Stopwatch _frameWatch = new();
    private int _dragFrames;
    private double _dragMs, _dragWorst;

    public FrameworkElement Root { get; }
    public Brush Background => _ui["Bg"];
    public Choice Result { get; private set; } = Choice.Close;
    public int PickedKey { get; private set; } = -1;

    /// <summary>넓은 창(목록 + 3D)인가.</summary>
    public bool Wide => _wide;

    /// <summary>3D 그림(없으면 null — 작은 창이거나 3D 를 만들지 못했다).</summary>
    public Model3DView? View3D => _view3d;

    /// <summary>창의 상태(초점 · 묶음) — 시험에서 본다.</summary>
    public OverlapBoard Board => _board;

    /// <summary>이 창에서 한 일(일지에 옮겨 적는다): 누른 줄 · 찍은 덩어리 · 초점이 어디로 갔는지 · 고친 쌍 · 그리는 데 걸린 시간.</summary>
    public List<string> Trail { get; } = new();

    // 시험에서 본다: 마우스 밑의 것을 적은 글 · 초점 띠의 글 · 그림 위의 알림(보이지 않으면 "") · 목록의 폭 · 고치기 줄이 보이는가.
    public string HoverText => _hoverPill.Visibility == Visibility.Visible ? _hover.Text : "";
    public string FocusText => string.Concat(_focusHead.Inlines.OfType<Run>().Select(run => run.Text)) + " | " + _focusSub.Text;
    public string MessageText => _toast.Visibility == Visibility.Visible ? _message.Text : "";
    public double SideWidth => _sideColumn?.ActualWidth ?? 0;
    public bool FixShown => _m.Fix != null && _fixRow.Visibility == Visibility.Visible;

    /// <summary>고치기의 사용자 모드인가(시험에서 처음 상태를 맞춘다).</summary>
    public static bool UserMode { get => s_userMode; set => s_userMode = value; }

    /// <summary>단추를 눌렀다(창을 닫을 때).</summary>
    public event Action? Done;

    public OverlapResultView(Model model, bool dark, string joinedText, string notNeededText)
    {
        _m = model;
        _ui = new UiTheme(dark);
        _joinedText = joinedText;
        _notNeededText = notNeededText;
        _board = new OverlapBoard(model.Rows, model.Mode == Mode.Joined);
        _wide = model.Rows.Count > 0 && model.Mode is Mode.Found or Mode.Joined;

        var dock = new DockPanel { Margin = _wide ? new Thickness(18, 14, 18, 14) : new Thickness(18, 16, 18, 14) };
        var root = new Border { Background = Background, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true, Focusable = true, FocusVisualStyle = null };
        root.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _ui["Text"]);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        if (!_wide)
        {
            Top(dock, Head());
            Bottom(dock, Buttons());
            var tiles = Tiles(compact: false);
            if (tiles != null) Top(dock, tiles);
            if (_m.SelectionOnly && _m.Mode == Mode.Clean) Top(dock, SelectionBanner());
            if (_m.Notes.Count > 0) Top(dock, NotesBanner());
            dock.Children.Add(new Border());
            return;
        }

        // ── 넓은 창: 머리(결과 + 숫자 칸) · 알림 · [왼쪽 목록 | 가르개 | 오른쪽 초점 띠 + 3D] · 단추 ──
        _headHost.Margin = new Thickness(0, 0, 0, 12);
        Top(dock, _headHost);
        if (_m.SelectionOnly) Top(dock, SelectionBanner());
        if (_m.Notes.Count > 0) Top(dock, NotesBanner());
        Bottom(dock, _buttonsHost);

        // 그림에 넘길 겉면: 부재 전부, 그 뒤에 덩어리(그릴 수 있는 것만).
        _members = _m.Meshes.Count;
        _drawn.AddRange(_m.Meshes);
        foreach (var row in _m.Rows.OrderBy(r => r.Key))
        {
            if (!row.HasBlob || row.Key < 0 || row.Key >= _m.Blobs.Count || _m.Blobs[row.Key] is not { } blob) continue;
            _blobAt[row.Key] = _drawn.Count;
            _blobKey.Add(row.Key);
            _drawn.Add(blob);
        }
        bool drawable = _m.SceneProblem.Length == 0 && _members > 0 && _m.Meshes.Any(mesh => mesh.Triangles > 0);
        if (drawable)
        {
            _view3d = new Model3DView(_drawn, dark, layered: true)
            {
                BandSelect = false,
                Shown = i => i < _members || BlobShown(_blobKey[i - _members]),
                StyleOf = StyleOf,
                ColorOf = ColorOf,
            };
            _view3d.Clicked += (index, _) => OnPicture(index);
            _view3d.Hovered += index =>
            {
                _hover.Text = index < 0 ? " " : Describe(index);
                _hoverPill.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
            };
            _view3d.Failed += ex => ShowMessage("3D 그림에서 오류가 났습니다(끌던 것은 그만두었습니다): " + ex.Message, true);
        }

        var middle = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var sideColumn = new ColumnDefinition { Width = new GridLength(WideSide), MinWidth = SideMin };
        var stageColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = StageMin };
        _sideColumn = sideColumn;
        middle.ColumnDefinitions.Add(sideColumn);
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterWidth) });
        middle.ColumnDefinitions.Add(stageColumn);
        var side = BuildSide();
        var right = BuildRight();
        Grid.SetColumn(right, 2);
        middle.Children.Add(side);
        AddSplitter(middle);
        middle.Children.Add(right);
        // 처음 자리를 잡으면 두 칸을 비율 폭으로 바꾼다 — 그 뒤로는 창을 줄이거나 늘려도 두 칸이 함께 줄고 늘어 그림이 창 밖으로 밀리지 않는다
        // (목록을 고정 폭으로 두면, 가르개로 넓힌 채 창을 줄일 때 그림 칸이 가장 작은 폭을 지키느라 창 밖으로 밀린다 — 시험에서 봄).
        bool starred = false;
        middle.SizeChanged += (_, _) =>
        {
            if (starred || sideColumn.ActualWidth <= 0 || stageColumn.ActualWidth <= 0) return;
            starred = true;
            sideColumn.Width = new GridLength(sideColumn.ActualWidth, GridUnitType.Star);
            stageColumn.Width = new GridLength(stageColumn.ActualWidth, GridUnitType.Star);
        };
        dock.Children.Add(middle);

        root.PreviewKeyDown += OnKey;
        root.PreviewMouseLeftButtonDown += OnPressAnything;
        RefreshChrome();
        RefreshList();
        RefreshFocus();
    }

    private static void Top(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Top); dock.Children.Add(child); }
    private static void Bottom(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Bottom); dock.Children.Add(child); }

    // 확인할 쌍: 결합 못 함 · 겹침이 남음. 결합한 뒤의 창은 결합하지 않은 쌍(다시 찾아 나온 새 쌍 · 다시 연 쌍)도 — 보드의 «끝나지 않은 쌍»과 같다
    // (core-verify R50 높음: 그것을 빼면 새 겹침이 남았는데 «모두 정리»가 뜬다).
    private int Problems => _m.Mode == Mode.Joined ? _board.Problems : _m.StillOverlap + _m.Failed;

    // 쌍의 수는 보드에서 읽는다(고친 뒤 다시 찾아 붙인 줄까지 — core-verify R50 낮음 8).
    private int Pairs => _board.Ordered.Count;

    // 결합한 뒤의 창에서 아직 결합하지 않은 쌍(다시 찾아 나온 새 쌍 · 다시 연 쌍).
    private int Pending => _board.Ordered.Count(r => !r.Fine && r.Status.Length == 0);

    // [우선순위대로 결합]이 거는 쌍(고치지 않았거나 · 못 했거나 · 겹침이 남은 쌍 — 여러 조각과 겹쳐 다시 검사할 쌍은 빼고).
    private int Left => _board.Joinable;

    // 결과가 하나라도 있다(결합한 뒤의 창이거나, 창에서 하나라도 고쳐 봤다).
    private bool Touched => _m.Mode == Mode.Joined || _m.Joined + _m.NotNeeded + _m.StillOverlap + _m.Failed > 0;

    // [전체 다시 검사]를 둘 것인가: 결합한 뒤의 창에는 두지 않는다 — 부르는 쪽은 그 뒤로는 처음부터 다시 돌지 않는다.
    private bool CanCheckAll => _m.CanCheckAll && _m.Mode != Mode.Joined;

    // ── 머리: 한눈에 보이는 결과 ──────────────────────────────────────────────────

    // sub = 제목 밑의 한 줄(작은 창에만 — 넓은 창에는 두지 않는다).
    private UIElement Head(bool margin = true)
    {
        (string tone, string glyph, string title, string sub) = _m.Mode switch
        {
            Mode.Found => ("Warn", UiTheme.IconWarn, $"겹친 부재 {Pairs}쌍을 찾았습니다", ""),
            Mode.Joined when Problems == 0 => ("Good", UiTheme.IconCheck, "겹친 자리를 모두 정리했습니다", ""),
            Mode.Joined => ("Warn", UiTheme.IconWarn, $"{Problems}쌍은 직접 확인해 주세요", ""),
            Mode.Clean => ("Good", UiTheme.IconCheck, "겹친 부재가 없습니다",
                _m.SelectionOnly ? "선택한 부재끼리는 두 번 세는 자리가 없습니다."
                : _m.LeanToFix + _m.LaterToFix + _m.SplitToDo > 0 ? "두 번 세는 자리가 없습니다."      // 결합 순서 바꾸기 · 두 덩이 나누기가 먼저다 — «바로 산출식»은 권하지 않는다
                : "두 번 세는 자리가 없습니다. 이대로 [수량산출]을 하면 됩니다."),
            _ => ("Neutral", UiTheme.IconWarn, "검사할 콘크리트 부재가 없습니다",
                _m.SelectionOnly ? "선택한 부재 가운데 콘크리트 부재가 없습니다. 선택을 풀고 다시 실행하면 모델 전체를 검사합니다."
                                 : "이 모델에서 콘크리트 부재를 찾지 못했습니다."),
        };

        var icon = UiTheme.Icon(glyph, 18);
        icon.Foreground = _ui[tone + "Fg"];
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = _ui[tone + "Bg"], Child = icon, VerticalAlignment = VerticalAlignment.Top };

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (sub.Length > 0)
            text.Children.Add(new TextBlock { Text = sub, Foreground = _ui["Sub"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 19 });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, margin ? 14 : 0) };
        if (sub.Length == 0) badge.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        head.Children.Add(text);
        return head;
    }

    // 넓은 창의 머리(결과 · 숫자 칸)와 아래 단추를 다시 짓는다(처음 · 창에서 고친 뒤 — 수가 바뀐다).
    private void RefreshChrome()
    {
        var head = new DockPanel();
        var numbers = Tiles(compact: true)!;
        DockPanel.SetDock(numbers, Dock.Right);
        head.Children.Add(numbers);
        head.Children.Add(Head(margin: false));
        _headHost.Child = head;
        _buttonsHost.Child = Buttons();
    }

    // ── 숫자 칸 ───────────────────────────────────────────────────────────────────

    // compact = 넓은 창의 머리 오른쪽에 나란히 놓는 좁은 칸.
    private FrameworkElement? Tiles(bool compact)
    {
        var tiles = new List<Border>();
        switch (_m.Mode)
        {
            case Mode.Found:
                tiles.Add(Tile("겹친 쌍", Pairs.ToString(CultureInfo.InvariantCulture), "쌍", "WarnFg"));
                if (Touched)
                {
                    // 창에서 고쳐 본 뒤: 정리된 쌍 · (확인할 쌍) · 남은 중복 체적.
                    tiles.Add(Tile("정리됨", (_m.Joined + _m.NotNeeded).ToString(CultureInfo.InvariantCulture), "쌍", "GoodFg"));
                    if (Problems > 0) tiles.Add(Tile("확인 필요", Problems.ToString(CultureInfo.InvariantCulture), "쌍", "WarnFg", "결합하지 못했거나 결합한 뒤에도 겹침이 남은 쌍"));
                    tiles.Add(Tile("남은 중복 체적", M3(_m.RemainingM3), "m³", _m.RemainingM3 >= 0.005 ? "WarnFg" : "GoodFg", $"처음 찾았을 때: {M3(_m.TotalM3)} m³"));
                }
                else tiles.Add(Tile("두 번 세는 체적", M3(_m.TotalM3), "m³", "WarnFg"));
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                break;
            case Mode.Joined:
                tiles.Add(Tile("결합", (_m.Joined + _m.NotNeeded).ToString(CultureInfo.InvariantCulture), "쌍", "GoodFg",
                    _m.NotNeeded > 0 ? $"이 가운데 {_m.NotNeeded}쌍은 앞선 결합으로 겹침이 이미 없어져 따로 걸지 않았습니다." : ""));
                // 결합하지 않은 쌍(다시 찾아 나온 새 겹침)도 겹침이 남은 쌍으로 센다.
                tiles.Add(Tile("겹침이 남음", (_m.StillOverlap + Pending).ToString(CultureInfo.InvariantCulture), "쌍", _m.StillOverlap + Pending > 0 ? "WarnFg" : "Sub",
                    Pending > 0 ? $"이 가운데 {Pending}쌍은 다시 찾아 나온 새 겹침으로 아직 결합하지 않았습니다." : ""));
                tiles.Add(Tile("결합 못 함", _m.Failed.ToString(CultureInfo.InvariantCulture), "쌍", _m.Failed > 0 ? "BadFg" : "Sub"));
                tiles.Add(Tile("남은 중복 체적", M3(_m.RemainingM3), "m³", _m.RemainingM3 >= 0.005 ? "WarnFg" : "GoodFg",
                    $"결합하기 전: {M3(_m.TotalM3)} m³"));
                break;
            case Mode.Clean:
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                tiles.Add(Tile("겹친 쌍", "0", "쌍", "GoodFg"));
                break;
            default:
                return null;
        }
        if (compact)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, -4, 0) };
            foreach (var tile in tiles)
            {
                tile.MinWidth = 116;
                tile.Padding = new Thickness(12, 6, 12, 7);
                line.Children.Add(tile);
            }
            return line;
        }
        var grid = new UniformGrid { Rows = 1, Margin = new Thickness(-4, 0, -4, 12) };
        foreach (var tile in tiles) grid.Children.Add(tile);
        return grid;
    }

    private Border Tile(string label, string value, string unit, string color, string tip = "")
    {
        var number = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom };
        number.Inlines.Add(new Run(value) { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _ui[color] });
        number.Inlines.Add(new Run(" " + unit) { FontSize = 12, Foreground = _ui["Sub"] });
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = _ui["Sub"], Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(number);
        var tile = new Border
        {
            Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(4, 0, 4, 0), Child = stack,
        };
        _ui.Tip(tile, tip);
        return tile;
    }

    private UIElement NotesBanner()
    {
        var stack = new StackPanel();
        foreach (string note in _m.Notes)
            stack.Children.Add(new TextBlock { Text = note, Foreground = _ui["WarnFg"], TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        return new Border { Background = _ui["WarnBg"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    // 선택한 부재만 검사했다는 알림(JACK 2026-10-14: 앞선 명령이 남긴 선택 때문에 "다시 실행하니 1쌍"만 나왔다) + [전체 다시 검사].
    private UIElement SelectionBanner()
    {
        var line = new DockPanel();
        if (CanCheckAll && _wide)           // 작은 창에서는 아래 단추 줄에 둔다
        {
            var all = _ui.Button("전체 다시 검사", "BtnSecondary", "선택과 상관없이 모델의 콘크리트 부재를 모두 다시 검사합니다(모델은 바꾸지 않습니다).", "checkAll");
            all.Margin = new Thickness(12, 0, 0, 0);
            all.Click += (_, _) => Finish(Choice.CheckAll, -1);
            DockPanel.SetDock(all, Dock.Right);
            line.Children.Add(all);
        }
        line.Children.Add(new TextBlock
        {
            Text = _wide
                ? $"선택한 부재 {_m.Checked}개만 검사했습니다"
                : $"Revit 에서 선택해 둔 부재 {_m.Checked}개만 검사했습니다 — 선택하지 않은 부재와의 겹침은 여기 없습니다." +
                  (CanCheckAll ? " 모델 전체를 보려면 아래의 [전체 다시 검사]를 누르세요." : " 모델 전체를 보려면 선택을 풀고 다시 실행하세요."),
            Foreground = _ui["AccentText"], TextWrapping = TextWrapping.Wrap, LineHeight = 19, VerticalAlignment = VerticalAlignment.Center,
        });
        var banner = new Border { Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 0, 12), Child = line };
        if (_wide) _ui.Tip(banner, "선택하지 않은 부재와의 겹침은 여기 없습니다.");
        return banner;
    }

    // ── 왼쪽: 쌍 목록(묶음) ───────────────────────────────────────────────────────

    private FrameworkElement BuildSide()
    {
        _sideTitle.FontWeight = FontWeights.SemiBold;     // 글은 RefreshList 가 적는다(고친 뒤 새 쌍이 붙으면 수가 는다)
        _sideTitle.FontSize = 14;
        _sideTitle.Margin = new Thickness(14, 10, 14, 8);
        _header.Margin = new Thickness(15, 0, 22, 6);
        var top = new StackPanel();
        top.Children.Add(_sideTitle);
        top.Children.Add(_header);
        var topLine = new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(0, 0, 0, 1), Child = top };

        _listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list };
        var inner = new DockPanel();
        DockPanel.SetDock(topLine, Dock.Top);
        inner.Children.Add(topLine);
        inner.Children.Add(_listScroll);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = inner,
        };
    }

    // 목록과 그림 사이의 가르개(JACK 2026-10-14 "목록표랑 3D 뷰 상자 사이를 마우스로 창 크기 조절되게") — 끌면 목록의 폭이 바뀐다.
    // 가르개 자체는 투명하고(마우스만 받는다) 가운데의 짧은 막대가 자리를 보여 준다. 둘 다 같은 칸(1)에 놓는다 — GridSplitter 는 제 부모 Grid 의 칸을 바꾼다.
    private void AddSplitter(Grid grid)
    {
        var grip = new Border
        {
            Width = 4, Height = 44, CornerRadius = new CornerRadius(2), Background = _ui["Line"], IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var splitter = new GridSplitter
        {
            Width = SplitterWidth, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = Brushes.Transparent, Cursor = Cursors.SizeWE, Focusable = false, ShowsPreview = false,
        };
        _ui.Mark(splitter, "끌어서 목록과 그림의 폭을 바꿉니다.", "splitter");
        bool dragging = false;
        void Paint() => grip.Background = dragging || splitter.IsMouseOver ? _ui["Accent"] : _ui["Line"];
        splitter.MouseEnter += (_, _) => Paint();
        splitter.MouseLeave += (_, _) => Paint();
        splitter.DragStarted += (_, _) => { dragging = true; Paint(); };
        splitter.DragCompleted += (_, _) =>
        {
            dragging = false;
            Paint();
            Note(string.Create(CultureInfo.InvariantCulture, $"폭 바꿈 → 목록 {SideWidth:0}"));
        };
        Grid.SetColumn(grip, 1);
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(grip);
        grid.Children.Add(splitter);
    }

    // 결과 칸을 둘 것인가: 결합한 뒤의 창이거나, 창에서 하나라도 고쳐 본 줄 · 고친 뒤 새로 나온 줄이 있다.
    private bool ShowStatus => _m.Mode == Mode.Joined || _board.Ordered.Any(r => r.Status.Length > 0 || r.New);

    // 목록을 다시 짓는다(묶음을 접거나 폈을 때 · 고쳤을 때 · 처음).
    private void RefreshList()
    {
        bool status = ShowStatus;
        bool joined = _m.Mode == Mode.Joined;
        _sideTitle.Text = $"겹친 자리 {_board.Ordered.Count}쌍";
        _header.Children.Clear();
        SetColumns(_header, status);
        HeaderCell(_header, 0, "겹친 체적", HorizontalAlignment.Right);
        HeaderCell(_header, 2, "남는 쪽");
        HeaderCell(_header, 4, joined ? "잘린 쪽" : "잘리는 쪽");
        if (status) HeaderCell(_header, 6, "결과");

        _list.Children.Clear();
        _rowOf.Clear();
        _cells.Clear();
        int at = 0;
        foreach (var group in _board.Groups)
        {
            _list.Children.Add(GroupHead(group, at++));
            if (group.Collapsed) continue;
            foreach (var row in group.Rows)
            {
                var button = PairRow(row, status);
                _rowOf[row.Key] = button;
                _list.Children.Add(button);
            }
        }
        MarkRows();
    }

    // 묶음의 머리: ▾ 슬래브 → 벽체 …… 10쌍 · 1.83 m³ (결합한 뒤: 확인 n)
    private Button GroupHead(OverlapGroup group, int index)
    {
        var chevron = UiTheme.Icon(group.Collapsed ? IconRight : UiTheme.IconDown, 9);
        chevron.Foreground = _ui["Sub"];
        chevron.Margin = new Thickness(0, 1, 7, 0);
        var count = new TextBlock { Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        count.Inlines.Add(new Run($"{group.Rows.Count}쌍 · {M3(group.TotalM3)} m³"));
        var line = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        DockPanel.SetDock(count, Dock.Right);
        line.Children.Add(count);
        if (group.Problems > 0)
        {
            var warn = _ui.Pill(new TextBlock { Text = $"확인 {group.Problems}", FontSize = 11.5, Foreground = _ui["WarnFg"] }, "WarnBg");
            warn.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(warn, Dock.Right);
            line.Children.Add(warn);
        }
        DockPanel.SetDock(chevron, Dock.Left);
        line.Children.Add(chevron);
        line.Children.Add(new TextBlock { Text = group.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Style = _ui.StyleOf("RowButton"), Content = line, Margin = new Thickness(0, index == 0 ? 0 : 4, 0, 0) };
        _ui.Mark(button, group.Collapsed ? "눌러서 이 묶음의 쌍을 폅니다." : "눌러서 이 묶음을 접습니다(쌍은 그대로 있습니다).",
                 "group." + index.ToString(CultureInfo.InvariantCulture));
        button.Click += (_, _) =>
        {
            group.Collapsed = !group.Collapsed;
            RefreshList();
        };
        return button;
    }

    // 칸: 체적 · 틈 · 남는 쪽 · 화살표 · 잘리는 쪽 · 틈 · 결과(결과 칸을 둘 때만)
    private static void SetColumns(Grid grid, bool withStatus)
    {
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 8 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 84 : 0) });
    }

    private void HeaderCell(Grid grid, int column, string text, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var cell = new TextBlock { Text = text, FontSize = 12, Foreground = _ui["Sub"], HorizontalAlignment = align };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private Button PairRow(OverlapRow row, bool withStatus)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 6) };
        SetColumns(grid, withStatus);

        var volume = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        volume.Inlines.Add(new Run(row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)) { FontWeight = FontWeights.SemiBold });
        volume.Inlines.Add(new Run(" m³") { FontSize = 11, Foreground = _ui["Sub"] });
        grid.Children.Add(volume);

        Place(grid, 2, MemberCell(row.Key, row.Winner, row.WinnerIndex, winner: true));
        var arrow = UiTheme.Icon(IconCut, 11);
        arrow.Foreground = _ui["Sub"];
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        Place(grid, 3, arrow);
        Place(grid, 4, MemberCell(row.Key, row.Loser, row.LoserIndex, winner: false));
        if (withStatus && row.Status.Length > 0) Place(grid, 6, StatusPill(row));
        else if (withStatus && row.New) Place(grid, 6, NewPill());
        else if (withStatus && _m.Mode == Mode.Joined) Place(grid, 6, PendingPill());   // 결합한 뒤 창에서 다시 연 쌍(core-verify R50 설치 전 B)

        var button = new Button { Style = _ui.StyleOf("RowPick"), Content = grid };
        string tip = $"{row.Winner}  →  {row.Loser}\n겹친 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³";
        _ui.Mark(button, tip, "pair" + row.Key.ToString(CultureInfo.InvariantCulture));
        button.Click += (_, _) =>
        {
            Note($"줄 {row.Key}");
            Focus(row.Key, zoom: true);
        };
        return button;
    }

    private static void Place(Grid grid, int column, UIElement child)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    // 줄 안의 부재 이름: 그 부재가 그림에 있으면 누를 수 있다 — 누르면 그 부재만 칠한다(JACK 2026-10-19 «객체 이름을 누르면 그 객체만 색상으로»).
    // 이름 밖의 줄을 누르면 쌍을 본다(안쪽 단추의 Click 은 줄로 올라가지 않게 막는다). 그림이 없으면 이름은 줄의 일부일 뿐이다(눌러도 같은 일 — 헛단추 금지).
    private UIElement MemberCell(int key, string name, int member, bool winner)
    {
        var stack = Member(name);
        if (_view3d == null || member < 0 || member >= _members) return stack;
        var cell = new Button { Style = _ui.StyleOf("CellPick"), Content = stack, Margin = new Thickness(-6, 0, 0, 0) };
        _ui.Mark(cell, "이 부재만 봅니다(다시 누르거나 줄의 다른 곳을 누르면 쌍을 봅니다).",
                 "pair" + key.ToString(CultureInfo.InvariantCulture) + (winner ? ".winner" : ".loser"));
        cell.Click += (_, e) =>
        {
            e.Handled = true;
            ShowOnly(key, member);
        };
        _cells.Add((key, member, winner, cell));
        return cell;
    }

    // "벽 WA_T300 (Id 1045724)" → 이름 한 줄 + 흐린 Id 한 줄.
    private StackPanel Member(string name)
    {
        var (main, id) = Split(name);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = main, TextTrimming = TextTrimming.CharacterEllipsis });
        if (id.Length > 0) stack.Children.Add(new TextBlock { Text = id, FontSize = 11, Foreground = _ui["Sub"] });
        return stack;
    }

    private static (string main, string id) Split(string name)
    {
        int at = name.LastIndexOf(" (Id ", StringComparison.Ordinal);
        return at > 0 && name.EndsWith(')') ? (name[..at], name[(at + 2)..^1]) : (name, "");
    }

    private Border StatusPill(OverlapRow row)
    {
        (string label, string tone) =
            row.Status == _joinedText ? ("결합됨", "Good")
            : row.Status == _notNeededText ? ("이미 풀림", "Neutral")
            : row.Status.StartsWith("결합 못 함", StringComparison.Ordinal) ? ("결합 못 함", "Bad")
            : row.Status.Length == 0 ? ("—", "Neutral")
            : row.Status.StartsWith(_joinedText + "(", StringComparison.Ordinal) ? ("확인 필요", "Warn") // 결합은 됐지만 다시 재지 못했다
            : ("겹침 남음", "Warn");
        // 결합으로 두 덩이가 된 잘린 쪽을 나눴는가(JACK 2026-10-14): 나눴으면 «결합·나눔», 못 나눴으면 «못 나눔»(까닭은 풍선 글).
        if (row.Status == _joinedText && row.Split.Length > 0)
            (label, tone) = row.Split.StartsWith(OverlapRow.SplitFail, StringComparison.Ordinal) ? ("못 나눔", "Warn") : ("결합·나눔", "Good");
        var pill = _ui.Pill(new TextBlock { Text = label, FontSize = 12, Foreground = _ui[tone + "Fg"] }, tone + "Bg");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        string detail = row.Status + (row.Split.Length > 0 ? "\n" + row.Split : "");
        if (!double.IsNaN(row.AfterM3) && row.AfterM3 >= OverlapShownM3)
            detail += $"\n결합한 뒤 남은 겹침: {row.AfterM3.ToString("0.000", CultureInfo.InvariantCulture)} m³";
        _ui.Tip(pill, detail);
        return pill;
    }

    // 고친 뒤 다시 찾아 새로 나온 줄의 딱지(결과가 생기면 결과 딱지가 대신한다 — JACK 2026-10-19).
    private Border NewPill()
    {
        var pill = _ui.Pill(new TextBlock { Text = "새로", FontSize = 12, Foreground = _ui["AccentText"] }, "AccentSoft");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        _ui.Tip(pill, "앞의 쌍을 고친 뒤 다시 찾아 새로 나온 겹침입니다.");
        return pill;
    }

    // 결합한 뒤의 창에서 아직 결합하지 않은 옛 줄(다시 찾기가 다시 연 쌍 — 겹침이 다시 생김)의 딱지.
    private Border PendingPill()
    {
        var pill = _ui.Pill(new TextBlock { Text = "아직 안 함", FontSize = 12, Foreground = _ui["WarnFg"] }, "WarnBg");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        _ui.Tip(pill, "앞의 쌍을 고친 뒤 다시 찾아 보니 겹침이 다시 있는 쌍입니다 — 아직 결합하지 않았습니다.");
        return pill;
    }

    private const double OverlapShownM3 = 0.0005; // 0.000 으로 찍히는 값은 풍선 글에 적지 않는다
    private const string IconCut = "";       // 오른쪽 화살표: 남는 쪽이 잘리는 쪽을 자른다
    private const string IconOpen = "";      // 파일 열기
    private const string IconRight = "";     // 접힌 묶음
    private const string IconLeft = "";

    // ── 오른쪽: 초점 띠 · 3D 그림 ─────────────────────────────────────────────────

    private FrameworkElement BuildRight()
    {
        var right = new DockPanel();

        // 초점 띠: 지금 무엇을 보고 있는지(전체 / 쌍 하나) + 옮겨 가기 + (쌍을 골랐을 때) 고치기. 그림 바로 위 — 보고 있는 것이 가장 중요하다.
        _focusHead.VerticalAlignment = VerticalAlignment.Center;
        _focusHead.TextWrapping = TextWrapping.Wrap;      // 좁은 창에서는 두 줄로(보고 있는 쌍의 이름이 잘리지 않게)
        _focusSub.Foreground = _ui["Sub"];
        _focusSub.FontSize = 12.5;
        _focusSub.TextTrimming = TextTrimming.CharacterEllipsis;
        _focusSub.Margin = new Thickness(0, 2, 0, 0);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_focusHead);
        texts.Children.Add(_focusSub);

        Button Small(string text, string tip, string id, Action act, string kind = "BtnSecondary", string icon = "")
        {
            var button = _ui.Button(text, kind, tip, id, icon);
            button.MinHeight = 30;
            button.Padding = new Thickness(10, 0, 10, 1);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Focusable = false;
            button.Click += (_, _) => act();
            return button;
        }
        _previous = Small("이전", "목록의 앞 쌍으로 갑니다.", "focus.previous", () => Step(-1), icon: IconLeft);
        _next = Small("다음", "목록의 다음 쌍으로 갑니다.", "focus.next", () => Step(+1), icon: IconRight);
        _showAll = Small("전체 보기", "겹친 자리를 모두 봅니다(Esc).", "focus.all", () => { Note("전체 보기"); ShowAll(); });
        _inRevit = Small("Revit 에서 보기", "이 창을 닫고, 지금 보고 있는 쌍의 두 부재를 Revit 에서 선택해 화면에 맞춥니다.", "focus.inRevit", () =>
        {
            if (_board.Focus < 0) return;
            Note($"Revit 에서 보기 {_board.Focus}");
            Finish(Choice.Pair, _board.Focus);
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_next);
        buttons.Children.Add(_showAll);
        buttons.Children.Add(_inRevit);
        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(texts);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(bar);
        if (_m.Fix != null) stack.Children.Add(BuildFixRow());
        var focus = new Border
        {
            Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), MinHeight = 54, Child = stack,
        };
        DockPanel.SetDock(focus, Dock.Top);
        right.Children.Add(focus);

        var stage = new Grid();
        if (_view3d != null)
        {
            // 색 풀이는 그림 바로 아래의 띠에 둔다(그림 위에 얹으면 좁은 창에서 보는 방향 단추와 겹친다).
            var legend = BuildLegend();
            DockPanel.SetDock(legend, Dock.Bottom);
            right.Children.Add(legend);
            stage.Children.Add(_view3d);
            stage.Children.Add(BuildLook());
            // 마우스 밑의 것: 그림의 왼쪽 아래에 뜨는 딱지(그림을 가리지 않게 마우스는 받지 않는다).
            _hover.FontSize = 12.5;
            _hover.TextTrimming = TextTrimming.CharacterEllipsis;
            _hover.Text = " ";
            Glass(_hoverPill, _hover, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            stage.Children.Add(_hoverPill);
        }
        else
        {
            // 3D 를 만들지 못했다: 까닭을 그 자리에 적는다(목록과 단추는 그대로 쓸 수 있다).
            var why = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
            why.Children.Add(new TextBlock { Text = "3D 그림을 보여 줄 수 없습니다", FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            why.Children.Add(new TextBlock
            {
                Text = _m.SceneProblem.Length > 0 ? _m.SceneProblem : "그릴 수 있는 부재의 형상이 없습니다.",
                Foreground = _ui["Sub"], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 19, Margin = new Thickness(0, 6, 0, 0),
            });
            stage.Children.Add(why);
        }
        // 알림(안 된 까닭): 그림의 위쪽 가운데.
        _message.TextWrapping = TextWrapping.Wrap;
        _message.LineHeight = 18;
        _message.FontSize = 12.5;
        Glass(_toast, _message, HorizontalAlignment.Center, VerticalAlignment.Top);
        _toast.Margin = new Thickness(10, 52, 10, 10);
        _toast.MaxWidth = 560;
        stage.Children.Add(_toast);
        // 그림을 둥근 테두리 안에 담는다: 모서리 밖으로 그림이 삐져나오지 않게 둥글게 자른다.
        stage.SizeChanged += (_, _) => stage.Clip = new RectangleGeometry(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight), 9, 9);
        right.Children.Add(new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), MinHeight = 200, Child = stage });
        return right;
    }

    // 초점 띠의 둘째 줄: 고른 쌍만 고치기(JACK 2026-10-14). [기본 | 사용자] + 단추 — 기본 = 우선순위대로 · 사용자 = 어느 부재에서 겹친 만큼을 뺄지 고른다.
    private UIElement BuildFixRow()
    {
        _basic = _ui.Segment("기본", "우선순위대로 고칩니다: " + _m.PriorityText, "fix.mode.basic");
        _user = _ui.Segment("사용자", "겹친 만큼을 어느 부재에서 뺄지 고릅니다.", "fix.mode.user");
        _basic.Focusable = _user.Focusable = false;
        _basic.IsChecked = !s_userMode;
        _user.IsChecked = s_userMode;
        _basic.Checked += (_, _) => SetUserMode(false);
        _user.Checked += (_, _) => SetUserMode(true);
        var modes = _ui.Segments(_basic, _user);
        modes.Margin = new Thickness(0, 0, 10, 0);
        var line = new DockPanel();
        DockPanel.SetDock(modes, Dock.Left);
        line.Children.Add(modes);
        line.Children.Add(_fixActions);
        _fixRow.BorderBrush = _ui["Line"];
        _fixRow.BorderThickness = new Thickness(0, 1, 0, 0);
        _fixRow.Margin = new Thickness(0, 8, 0, 0);
        _fixRow.Padding = new Thickness(0, 8, 0, 0);
        _fixRow.Child = line;
        _fixRow.Visibility = Visibility.Collapsed;
        return _fixRow;
    }

    private void SetUserMode(bool on)
    {
        if (s_userMode == on) return;
        s_userMode = on;
        Note(on ? "고치기: 사용자 모드" : "고치기: 기본 모드");
        RefreshFix();
    }

    // 고치기 줄을 지금의 초점에 맞춘다: 쌍을 골랐을 때만 보인다. 끝난 쌍이면 결과 딱지만, 아니면 모드에 맞는 단추.
    private void RefreshFix()
    {
        if (_m.Fix == null) return;
        SetPreview(-1);
        _fixActions.Children.Clear();
        var row = _board.Focused;
        _fixRow.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        if (row == null) return;
        if (row.Fine || row.Blocked) { _fixActions.Children.Add(StatusPill(row)); return; }   // 끝난 쌍 · 이 창에서 고칠 수 없는 쌍(여러 조각과 겹침)
        var (winner, loser) = ShortNames(row.Winner, row.Loser);
        if (!s_userMode)
            _fixActions.Children.Add(FixButton("이 쌍 고치기", $"{winner} 은 그대로 두고 {loser} 에서 겹친 만큼을 뺍니다(형상 결합 · 닫은 뒤 Ctrl+Z 한 번으로 되돌림).",
                "fix.one", cutWinner: false, cut: row.LoserIndex, primary: true));
        else
        {
            // 잘린 쪽을 나눈 쌍은 남는 쪽을 맞바꿀 수 없다 — 그 단추를 두지 않는다(눌러도 거절만 뜨는 단추 금지 · R37 revit R1).
            if (!row.NoSwap)
                _fixActions.Children.Add(FixButton(winner + " 에서 빼기", $"{loser} 은 그대로 두고 {winner} 에서 겹친 만큼을 뺍니다(우선순위와 반대).",
                    "fix.cutWinner", cutWinner: true, cut: row.WinnerIndex));
            _fixActions.Children.Add(FixButton(loser + " 에서 빼기", $"{winner} 은 그대로 두고 {loser} 에서 겹친 만큼을 뺍니다(우선순위대로).",
                "fix.cutLoser", cutWinner: false, cut: row.LoserIndex));
        }
        if (row.Status.Length > 0)                        // 앞서 고쳐 봤지만 끝나지 않았다(못 함 · 겹침 남음)
        {
            var pill = StatusPill(row);
            pill.Margin = new Thickness(4, 0, 0, 0);
            _fixActions.Children.Add(pill);
        }
    }

    // 단추에 적을 두 부재의 짧은 이름: 이름만, 둘이 같으면 Id 를 붙인다.
    private static (string winner, string loser) ShortNames(string winner, string loser)
    {
        var (w, wid) = Split(winner);
        var (l, lid) = Split(loser);
        return w == l ? ($"{w} {wid}", $"{l} {lid}") : (w, l);
    }

    // cut = 이 단추로 겹친 만큼이 빠질 부재(그림의 차례) — 마우스를 올린 동안 고친 뒤의 모습으로 그린다(SetPreview).
    private Button FixButton(string text, string tip, string id, bool cutWinner, int cut, bool primary = false)
    {
        var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        var button = new Button
        {
            Style = _ui.StyleOf(primary ? "BtnPrimary" : "BtnSecondary"), Content = label,
            MinHeight = 30, Padding = new Thickness(12, 0, 12, 1), Margin = new Thickness(0, 0, 6, 0), Focusable = false,
        };
        _ui.Mark(button, tip, id);
        button.MouseEnter += (_, _) => SetPreview(cut, cutWinner);
        button.MouseLeave += (_, _) => SetPreview(-1);
        button.Click += (_, _) => DoFix(cutWinner);
        return button;
    }

    // 고치기 단추에 마우스를 올렸다(member = 잘릴 부재 · −1 = 뗐다): 고친 뒤의 모습(JACK 2026-10-19 «갖다 대면 변환 후의 모습 · 한쪽으로 아예 들어간»).
    // 잘릴 부재는 남을 부재를 뺀 모양(부르는 쪽이 Revit 으로 잘라 본 것)으로 잠깐 바꿔 그리고 그 쌍의 덩어리는 숨긴다 — 겹친 부위가 남을 부재 안으로 들어간다.
    // 두 부재의 색은 그대로다(JACK 고름 «색은 그대로»). 모양을 받지 못하면 덩어리를 남을 부재의 색으로 칠한다(ColorOf). 받은 모양은 고칠 때까지 다시 쓴다.
    private void SetPreview(int member, bool cutWinner = false)
    {
        if (_preview == member || (_busy && member >= 0)) return;     // 고치는 중에는 Revit 을 부르지 않는다(R51 revit-verify ②)
        if (member >= 0) EndSolo("고치기 단추에 마우스");             // 떼도 «부재만 보기»로 돌아가지 않는다(JACK 2026-10-19)
        EndShape();
        _preview = member;
        if (member >= 0 && _view3d != null && _m.Preview != null && _board.Focused is { } row)
        {
            if (!_cutShapes.TryGetValue((row.Key, cutWinner), out var shape))
            {
                var watch = Stopwatch.StartNew();
                try { shape = _m.Preview(row.Key, cutWinner); }
                catch (Exception ex) { Note("미리보기 오류\t" + ex); shape = (null, -1); }
                _cutShapes[(row.Key, cutWinner)] = shape;
                string got = shape.mesh == null ? "못 받음" : shape.at != member ? $"다른 칸({shape.at} ≠ {member})" : $"세모 {shape.mesh.Triangles}";
                Note($"미리보기 {row.Key}\t{(cutWinner ? "남는 쪽에서 빼기" : "잘리는 쪽에서 빼기")}\t{got}\t{watch.ElapsedMilliseconds} ms");
            }
            if (shape.mesh != null && shape.at == member)
            {
                _shaped = member;
                try { _view3d.Replace(member, shape.mesh); }
                catch (Exception ex)
                {
                    _shaped = -1;
                    Note($"미리보기 모양을 그리지 못함 {member}\t" + ex);
                }
            }
        }
        _view3d?.Refresh();
    }

    // 미리보기로 바꿔 둔 겉면을 그림이 실제로 가진 것(_drawn)으로 돌린다. 돌리지 못하면 _shaped 를 남겨 다음에 다시 돌린다 —
    // 그동안 Apply 는 그 칸의 _drawn 을 그림에서 읽지 않는다(미리보기 겉면이 _drawn 에 굳지 않게 · R51 revit-verify ③).
    private void EndShape()
    {
        if (_shaped < 0) return;
        int at = _shaped;
        try
        {
            _view3d?.Replace(at, _drawn[at]);
            _shaped = -1;
        }
        catch (Exception ex) { Note($"미리보기 모양을 돌리지 못함 {at}\t" + ex); }
    }

    // 덩어리를 그리는가: 덩어리가 있는 쌍 — 미리보기로 잘릴 부재의 모양을 바꿔 둔 동안 그 쌍의 덩어리는 숨긴다(겹친 부위가 남을 부재 안으로 들어간 모습).
    private bool BlobShown(int key) => _board.LookOf(key) != BlobLook.Hidden && !(_shaped >= 0 && key == _board.Focus);

    /// <summary>고른 쌍을 고친다(고치기 단추와 같다) — cutWinner = 남는 쪽에서 겹친 만큼을 뺀다. 고칠 수 없으면(쌍을 안 골랐거나 끝난 쌍) 아무것도 하지 않는다.</summary>
    public void FixFocused(bool cutWinner) => DoFix(cutWinner);

    private void DoFix(bool cutWinner)
    {
        if (_busy || _m.Fix == null || _board.Focused is not { } row || row.Fine || row.Blocked) return;
        Note($"고치기 {row.Key}\t{(s_userMode ? (cutWinner ? "사용자: 남는 쪽에서 빼기" : "사용자: 잘리는 쪽에서 빼기") : "기본")}");
        SetPreview(-1);                                   // 미리보기로 바꿔 둔 겉면을 먼저 돌린다(고친 결과의 겉면이 그 위에 온다)
        var watch = Stopwatch.StartNew();
        var cursor = Mouse.OverrideCursor;
        Fixed result;
        // Revit 이 결합하는 동안 창을 잠근다(「위치·부위 정하기」와 같은 꼴): 그사이 Revit 이 메시지를 돌려도 누르기 · 글쇠 · 닫기가 들어오지 않게.
        _busy = true;
        Root.IsHitTestVisible = false;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            result = _m.Fix(row.Key, cutWinner);
        }
        catch (Exception ex)
        {
            // 부르는 쪽은 결합 뒤의 오류를 안에서 받아 돌려준다 — 여기까지 샌 것이면 모델이 바뀌었는지 모른다: 바뀌었을 수 있다고 둔다.
            _changedModel = true;
            Note("고치기 오류\t" + ex);
            ShowMessage("고치는 중 오류가 났습니다: " + ex.Message, true);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = cursor;
            Root.IsHitTestVisible = true;
            _busy = false;
        }
        _changedModel |= result.Changed;
        try { Apply(result); }
        catch (Exception ex)
        {
            // 모델은 이미 고쳐졌다(또는 바뀐 것이 없다) — 창에 옮기다 난 오류만 알린다.
            Note("고친 결과를 창에 옮기다 오류\t" + ex);
            ShowMessage("고친 결과를 그리다 오류가 났습니다" + (result.Changed ? "(결합은 모델에 들어갔습니다)" : "") + ": " + ex.Message, true);
        }
        Note($"고친 뒤 {row.Key}\t{_board.Row(row.Key)?.Status}\t바뀐 줄 {result.Rows.Count} · 겉면 {result.Meshes.Count}\t{watch.ElapsedMilliseconds} ms");
    }

    /// <summary>고치는 중인가(시험에서 본다).</summary>
    public bool Busy => _busy;

    // 고친 결과를 창에 옮긴다: 줄(제자리에서) · 다시 찾아 새로 나온 줄(제 묶음 맨 위) · 고친 부재의 겉면 · 바뀐 덩어리 · 숫자 칸 · 단추 · 그림 · 알림.
    private void Apply(Fixed result)
    {
        // 새 줄 먼저(목록에 없는 번호만 · 같은 번호가 두 번 오면 앞의 것) — 같은 번호가 바뀐 줄에도 오면 그다음의 Replace 가 받는다(core-verify R50 중간 2).
        _cutShapes.Clear();                               // 모델이 바뀌었을 수 있다 — 받아 둔 미리보기 모양은 다시 받는다
        var fresh = result.Added.Where(r => _board.Row(r.Key) == null).GroupBy(r => r.Key).Select(g => g.First()).ToList();
        int added = _board.AddRange(fresh);
        int refused = 0;
        foreach (var row in result.Rows)
            if (!_board.Replace(row)) { refused++; Note($"줄을 바꾸지 못함 {row.Key}"); }
        string drawProblem = "";
        if (_view3d != null)
        {
            foreach (var (at, mesh) in result.Meshes)
            {
                if (at < 0 || at >= _members) continue;
                try { _view3d.Replace(at, mesh); }
                catch (Exception ex)
                {
                    Note($"겉면을 바꾸지 못함 {at}\t" + ex);
                    drawProblem = ReferenceEquals(_view3d.MeshAt(at), mesh)
                        ? "고친 부재를 다시 그리다 오류가 났습니다: " + ex.Message
                        : "고친 부재를 새 모양으로 그리지 못했습니다(옛 모양으로 보입니다): " + ex.Message;
                }
                // 창의 목록은 그림이 실제로 가진 겉면에 맞춘다 — Replace 가 어디서 던졌든 확대할 상자가 그림과 같게.
                // 다만 미리보기 겉면이 아직 그림에 남은 칸(돌리기 실패)은 받은 겉면으로 — 다음 EndShape 가 그것으로 돌린다.
                _drawn[at] = at == _shaped ? mesh : _view3d.MeshAt(at);
            }
            // 덩어리: 그림에 있던 쌍은 제자리에서 바꾸고(null = 빈 겉면 — 그 줄은 덩어리 없음이라 그리지 않는다), 없던 쌍은 끝에 붙여 한 번에 다시 넘긴다
            // (SetMeshes 는 있던 겉면을 다시 만들지 않는다). 붙인 차례는 _blobKey · _blobAt 와 늘 같다 — 그림이 실패해도 그대로 둔다(Shown 이 그 차례로 본다).
            bool appended = false;
            foreach (var (key, blob) in result.Blobs)
            {
                if (_blobAt.TryGetValue(key, out int at))
                {
                    var mesh = blob ?? new MeshData();
                    try { _view3d.Replace(at, mesh); }
                    catch (Exception ex)
                    {
                        Note($"덩어리를 바꾸지 못함 {key}\t" + ex);
                        drawProblem = "겹친 부위를 새로 그리지 못했습니다: " + ex.Message;
                    }
                    _drawn[at] = _view3d.MeshAt(at);
                }
                else if (blob is { Triangles: > 0 })
                {
                    _blobAt[key] = _drawn.Count;
                    _blobKey.Add(key);
                    _drawn.Add(blob);
                    appended = true;
                }
            }
            if (appended)
                try { _view3d.SetMeshes(_drawn); }
                catch (Exception ex)
                {
                    Note("새 덩어리를 붙이지 못함\t" + ex);
                    drawProblem = "새로 나온 겹친 부위를 그리지 못했습니다: " + ex.Message;
                }
            // 줄의 «덩어리 있음»을 그림이 실제로 가진 것에 맞춘다(core-verify R50 중간 4) — 없앤 · 세모 0 인 덩어리의 줄은 그리지 않고 초점 띠에 «그림 없음».
            foreach (int key in result.Blobs.Keys)
            {
                if (_board.Row(key) is not { } row) continue;
                bool drawn = _blobAt.TryGetValue(key, out int at) && _drawn[at].Triangles > 0;
                if (row.HasBlob != drawn) _board.Replace(row with { HasBlob = drawn });
            }
        }
        if (added + result.Gone > 0 || result.Blobs.Count > 0)
            Note($"다시 찾기 반영\t새로 {added} · 없어짐 {result.Gone} · 덩어리 {result.Blobs.Count}");
        _m.Joined = result.Joined;
        _m.StillOverlap = result.StillOverlap;
        _m.Failed = result.Failed;
        _m.NotNeeded = result.NotNeeded;
        _m.RemainingM3 = result.RemainingM3;
        if (result.SplitToDo >= 0) _m.SplitToDo = result.SplitToDo;   // 단추의 «나누기 m개»(R38 core L-4)
        RefreshChrome();
        RefreshList();
        RefreshFocus();
        if (added > 0) BringRowIntoView();                // 보던 줄 위에 새 줄이 끼면 밀려 내려간다(core-verify R50 낮음 7)
        var problems = new List<string>();
        if (result.Problem.Length > 0) problems.Add(result.Problem);
        if (refused > 0) problems.Add($"목록의 {refused}줄에 고친 결과를 옮기지 못했습니다 — 창을 닫고 다시 실행해 확인해 주세요.");
        if (drawProblem.Length > 0) problems.Add(drawProblem);
        // 다시 찾기로 달라진 것(JACK 2026-10-19 «새로 생긴 겹침 n쌍 · 없어진 겹침 m쌍» — 0 인 쪽은 뺀다).
        var news = new List<string>();
        if (added > 0) news.Add($"새로 생긴 겹침 {added}쌍");
        if (result.Gone > 0) news.Add($"없어진 겹침 {result.Gone}쌍");
        string said = string.Join(" · ", news);
        if (problems.Count > 0) ShowMessage(string.Join("\n", (said.Length > 0 ? new[] { said } : Array.Empty<string>()).Concat(problems)), true);
        else if (said.Length > 0) ShowMessage(said);
    }

    // 그림 위에 뜨는 딱지의 틀(처음에는 숨겨 둔다).
    private void Glass(Border pill, UIElement child, HorizontalAlignment across, VerticalAlignment down)
    {
        pill.Background = _ui["Glass"];
        pill.BorderBrush = _ui["Line"];
        pill.BorderThickness = new Thickness(1);
        pill.CornerRadius = new CornerRadius(8);
        pill.Padding = new Thickness(11, 5, 11, 6);
        pill.Margin = new Thickness(10);
        pill.HorizontalAlignment = across;
        pill.VerticalAlignment = down;
        pill.IsHitTestVisible = false;
        pill.Visibility = Visibility.Collapsed;
        pill.Child = child;
    }

    // 그림의 오른쪽 위에 떠 있는 줄: 보는 방향 · 화면 맞춤(「위치·부위 정하기」와 같은 모양).
    private FrameworkElement BuildLook()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, preset) in new[] { ("등각", ViewPreset.Iso), ("위", ViewPreset.Top), ("앞", ViewPreset.Front), ("뒤", ViewPreset.Back), ("왼쪽", ViewPreset.Left), ("오른쪽", ViewPreset.Right) })
        {
            var button = _ui.Button(name, "BtnSeg", name + "에서 봅니다.", "look." + preset);
            button.MinHeight = 26;
            button.Padding = new Thickness(9, 0, 9, 1);
            button.Focusable = false;
            var captured = preset;
            button.Click += (_, _) => Look(captured);
            line.Children.Add(button);
        }
        line.Children.Add(new Border { Width = 1, Background = _ui["Line"], Margin = new Thickness(4, 5, 4, 5) });
        var fit = _ui.Button("화면 맞춤", "BtnSeg", "보고 있는 것이 다 보이게 맞춥니다(가운데 단추를 두 번 눌러도 됩니다).", "btn.fit");
        fit.MinHeight = 26;
        fit.Padding = new Thickness(9, 0, 9, 1);
        fit.Focusable = false;
        fit.Click += (_, _) => Frame();
        line.Children.Add(fit);
        return new Border
        {
            Background = _ui["Glass"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = line,
        };
    }

    // 그림 바로 아래의 띠: 색 풀이(짧은 이름표만 — 마우스 쓰는 법 · 풀이는 풍선 글로).
    private FrameworkElement BuildLegend()
    {
        var line = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
        void Item((byte r, byte g, byte b) color, string text, bool outline = false)
        {
            var swatch = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = outline ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)), BorderThickness = new Thickness(outline ? 1.5 : 0),
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(item);
        }
        Item(BlobColor, "겹친 부위");
        Item(WinnerColor, "남는 쪽");
        Item(LoserColor, "잘리는 쪽");
        Item(_view3d!.GhostEdge, "나머지", outline: true);
        var legend = new Border { Padding = new Thickness(2, 7, 2, 0), Child = line };
        _ui.Mark(legend,
            "겹친 부위는 다른 부재에 가려져 있어도 보입니다. 두 부재는 쌍을 고르면 칠합니다 · 목록의 부재 이름을 누르면 그 부재만 칠합니다.\n" +
            "고치면 그 부위가 남는 쪽의 색(주황)이 됩니다 · 고치기 단추에 마우스를 올리면 고친 뒤의 모양(겹친 부위가 남을 부재 안으로)이 미리 보입니다 · 회색 = 앞선 결합으로 이미 풀린 자리.\n" +
            "돌리기 = 오른쪽 끌기 · 옮기기 = 가운데 끌기 · 확대 = 굴림" +
            (_m.Mode == Mode.Joined ? "\n결합한 뒤: 겹친 부위는 결합하기 전에 잰 모양입니다(다시 찾아 나온 쌍은 그때 잰 모양)." : ""), "legend");
        return legend;
    }

    // 그리는 방식: 덩어리는 맨 위(외곽선만으로 그릴 때는 둘레 부재처럼), 부재는 칠할 때만 옅은 면, 아니면 외곽선만.
    // 부재 이름을 눌렀으면(_solo) 그 부재만 옅은 면이고 나머지는 덩어리까지 외곽선만(고치기 단추에 마우스를 올리면 풀린다 — SetPreview).
    private MeshStyle StyleOf(int index)
    {
        bool solo = _solo >= 0 && _preview < 0;
        if (index >= _members) return solo || _board.LookOf(_blobKey[index - _members]) == BlobLook.Faint ? MeshStyle.Ghost : MeshStyle.Top;
        if (solo) return index == _solo ? MeshStyle.Tint : MeshStyle.Ghost;
        return _board.RoleOf(index) == OverlapRole.None ? MeshStyle.Ghost : MeshStyle.Tint;
    }

    // 그 겉면의 색: 덩어리 = 빨강(고친 자리 = 남는 쪽의 주황 · 이미 풀린 자리 = 회색), 부재 = 남는 쪽 주황 / 잘리는 쪽 초록.
    // 고치기 단추에 마우스를 올린 동안에도 두 부재의 색은 그대로다(JACK 2026-10-19 «색은 그대로»). 잘릴 부재의 모양을 받지 못해 그 쌍의 덩어리가
    // 보이면 남을 부재의 색으로 칠한다(겹친 부위가 그 부재의 것이 된다).
    // (외곽선만인 것의 색은 쓰이지 않는다.)
    private (byte r, byte g, byte b) ColorOf(int index)
    {
        if (index >= _members)
        {
            int key = _blobKey[index - _members];
            if (_preview >= 0 && key == _board.Focus) return _board.RoleOf(_preview) == OverlapRole.Winner ? LoserColor : WinnerColor;
            return _board.LookOf(key) switch { BlobLook.Done => WinnerColor, BlobLook.Gone => LooseColor, _ => BlobColor };
        }
        return _board.RoleOf(index) == OverlapRole.Winner ? WinnerColor : LoserColor;
    }

    // 마우스 밑의 것을 적은 글.
    private string Describe(int index)
    {
        if (index >= _members)
        {
            var row = _board.Row(_blobKey[index - _members]);
            if (row == null) return " ";
            return $"겹친 부위 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³ — {Split(row.Winner).main} → {Split(row.Loser).main}";
        }
        string name = index < _m.MemberNames.Count ? _m.MemberNames[index] : "";
        int pairs = _board.PairsOf(index).Count;
        return pairs > 1 ? $"{name} · 겹친 쌍 {pairs}개" : name;
    }

    // ── 초점 ─────────────────────────────────────────────────────────────────────

    // 그림을 찍었다: 덩어리(맨 위에 그려지므로 먼저 닿는다)면 그 쌍, 색칠된 부재면 그 부재가 든 쌍.
    private void OnPicture(int index)
    {
        if (index < 0 || _view3d == null) return;     // 빈 곳 · 외곽선만인 것: 그대로 둔다(Esc · [전체 보기]로 돌아간다)
        int before = _board.Focus;
        if (index >= _members)
        {
            // 그 자리의 덩어리 전부(가까운 것부터). 외곽선만인 덩어리도 넣는다 — 쌍을 고르면 다른 덩어리는 외곽선만이 되지만,
            // 같은 자리를 다시 누르면 그 아래에 겹친 덩어리로 돌아야 한다(세 부재가 한 자리에서 겹친 경우).
            var (origin, direction) = _view3d.Camera.Ray(_view3d.ClickPoint.X, _view3d.ClickPoint.Y);
            var keys = Picker.PickAll(_drawn, i => i >= _members && _board.LookOf(_blobKey[i - _members]) != BlobLook.Hidden, origin, direction)
                .Select(hit => _blobKey[hit.index - _members]).ToList();
            _board.ClickBlobs(keys);
            Note($"덩어리 찍기 [{string.Join(",", keys)}] → {_board.Focus}");
        }
        else
        {
            _board.ClickMember(index);
            Note($"부재 찍기 {(index < _m.MemberNames.Count ? Split(_m.MemberNames[index]).id : index.ToString(CultureInfo.InvariantCulture))} → {_board.Focus}");
        }
        if (_board.Focus == before) return;
        RefreshList();          // 접혀 있던 묶음이 펴졌을 수 있다
        RefreshFocus();
        BringRowIntoView();
    }

    /// <summary>그 쌍만 본다(목록의 줄을 누른 것과 같다). zoom = 그림도 그 자리로 간다.</summary>
    public bool Focus(int key, bool zoom)
    {
        bool wasCollapsed = _board.GroupOf(key)?.Collapsed == true;
        if (!_board.SetFocus(key)) return false;
        if (wasCollapsed) RefreshList();
        RefreshFocus();
        BringRowIntoView();
        if (zoom) Frame();
        return true;
    }

    // 목록의 부재 이름을 눌렀다: 그 쌍을 고르고 그 부재만 칠한다. 칠한 이름을 다시 누르면 쌍을 본다.
    // 보는 자리는 그대로다(JACK 2026-10-19 «뷰범위가 초기화되는데 현재 기준으로 — 이름을 바꿔 가며 누르면서 차이를 본다»).
    private void ShowOnly(int key, int member)
    {
        string id = member < _m.MemberNames.Count ? Split(_m.MemberNames[member]).id : member.ToString(CultureInfo.InvariantCulture);
        if (_board.Focus == key && _solo == member)
        {
            Note($"부재만 보기 풀기 {key}");
            Focus(key, zoom: false);
            return;
        }
        Note($"부재만 보기 {key}\t{id}");
        if (!Focus(key, zoom: false)) return;           // 초점을 옮기며 앞의 «부재만 보기»는 풀린다
        if (_drawn[member].Box.IsEmpty)
        {
            ShowMessage("이 부재는 그림에 보여 줄 형상이 없습니다 — [Revit 에서 보기]로 확인해 주세요.", true);
            return;
        }
        _solo = member;
        _view3d?.Refresh();
        MarkRows();
    }

    // «부재만 보기»를 푼다 — 그 줄을 누른 것처럼 쌍을 본다(보는 자리는 그대로). 이름 밖의 단추를 누르거나 고치기 단추에 마우스를 올리면
    // (JACK 2026-10-19 «객체 이름 외 다른 단추나 마우스 올리기에는 해당 줄을 선택했던 것처럼 풀리게»).
    private void EndSolo(string why)
    {
        if (_solo < 0) return;
        _solo = -1;
        Note($"부재만 보기 풀림\t{why}");
        _view3d?.Refresh();
        MarkRows();
    }

    // 창의 어느 단추든 누르기 직전(눌린 단추의 처리기보다 먼저): 부재 이름이 아니면 «부재만 보기»를 푼다 — [화면 맞춤] · 보는 방향도 쌍을 기준으로 한다.
    // 그림(돌리기 · 옮기기 · 확대)과 목록 위로 마우스를 옮기는 것은 풀지 않는다(이름을 바꿔 가며 누를 수 있게).
    private void OnPressAnything(object sender, MouseButtonEventArgs e)
    {
        if (_solo < 0) return;
        for (var at = e.OriginalSource as DependencyObject; at != null && !ReferenceEquals(at, Root); at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is not ButtonBase button) continue;
            if (button is RepeatButton || _cells.Any(c => ReferenceEquals(c.cell, button))) return;   // 목록을 굴리는 막대 · 부재 이름은 풀지 않는다
            EndSolo("단추 " + (System.Windows.Automation.AutomationProperties.GetAutomationId(button) is { Length: > 0 } name ? name : button.GetType().Name));
            return;
        }
    }

    /// <summary>목록에서 이름을 눌러 그 부재만 보고 있으면 그 부재(그림의 차례), 아니면 -1 — 시험에서 본다.</summary>
    public int Solo => _solo;

    private void Step(int by)
    {
        int key = by > 0 ? _board.Next() : _board.Previous();
        if (key < 0) return;
        Note($"{(by > 0 ? "다음" : "이전")} → {key}");
        RefreshList();
        RefreshFocus();
        BringRowIntoView();
        Frame();
    }

    /// <summary>전체 보기로 돌아간다.</summary>
    public void ShowAll()
    {
        _board.ClearFocus();
        RefreshFocus();
        Frame();
    }

    // 보고 있는 것에 화면을 맞춘다: 초점이 있으면 그 자리(둘레가 보이게 넉넉히), 없으면 전부.
    private void Frame()
    {
        if (_view3d == null) return;
        if (_board.Focus < 0) { _view3d.Fit(); return; }
        var box = _board.ZoomBox(_board.Focus, i => i >= 0 && i < _members ? _drawn[i].Box : Box3.Empty,
                                 key => _blobAt.TryGetValue(key, out int at) ? _drawn[at].Box : Box3.Empty);
        if (box is { } target) _view3d.ZoomTo(target, OverlapBoard.ZoomMargin, OverlapBoard.ZoomWidth(target));
        else ShowMessage("이 쌍은 그림에 보여 줄 형상이 없습니다 — [Revit 에서 보기]로 확인해 주세요.", true);
    }

    private void Look(ViewPreset preset)
    {
        if (_view3d == null) return;
        _view3d.Camera.Look(preset);
        Frame();
    }

    // 초점이 바뀌었다: 그림 · 초점 띠 · 줄의 표시 · 고치기 줄을 맞춘다.
    private void RefreshFocus()
    {
        EndShape();
        _preview = -1;
        _solo = -1;
        _view3d?.Refresh();
        MarkRows();
        HideMessage();
        var row = _board.Focused;
        _focusHead.Inlines.Clear();
        if (row == null)
        {
            _focusHead.Inlines.Add(new Run(_m.Mode == Mode.Joined ? $"결합한 뒤 {Pairs}쌍 전체" : $"겹친 자리 {Pairs}곳 전체") { FontWeight = FontWeights.SemiBold, FontSize = 14 });
            _focusSub.Text = "";
        }
        else
        {
            var (winner, winnerId) = Split(row.Winner);
            var (loser, loserId) = Split(row.Loser);
            // ■ = 그림의 색(남는 쪽 주황 · 잘리는 쪽 초록) — 이름과 그림의 부재를 잇는다.
            void Side(string label, (byte r, byte g, byte b) color, string name, string id)
            {
                _focusHead.Inlines.Add(new Run("■ ") { Foreground = new SolidColorBrush(Color.FromRgb(color.r, color.g, color.b)), FontSize = 13 });
                _focusHead.Inlines.Add(new Run(label + " ") { Foreground = _ui["Sub"], FontSize = 12.5 });
                _focusHead.Inlines.Add(new Run(name) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
                if (id.Length > 0) _focusHead.Inlines.Add(new Run("  " + id) { Foreground = _ui["Sub"], FontSize = 12 });
            }
            Side("남는 쪽", WinnerColor, winner, winnerId);
            _focusHead.Inlines.Add(new Run("   →   ") { Foreground = _ui["Sub"] });
            Side("잘리는 쪽", LoserColor, loser, loserId);
            int at = _board.Ordered.ToList().FindIndex(r => r.Key == row.Key) + 1;
            string sub = $"겹친 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³ · {row.WinnerPart} → {row.LoserPart} · {at} / {_board.Ordered.Count}";
            if (_m.Fix == null && row.Status.Length > 0) sub += " · " + row.Status;    // 고치기 줄이 있으면 결과는 그 줄의 딱지로 보인다
            if (row.Split.Length > 0) sub += " · " + row.Split;                         // 조각 n개 / 나누기 못 함: 까닭
            if (_view3d != null && !row.HasBlob) sub += " · 겹친 부위 그림 없음";
            _focusSub.Text = sub;
        }
        _focusSub.Visibility = _focusSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_showAll != null) _showAll.IsEnabled = row != null;
        if (_inRevit != null) _inRevit.IsEnabled = row != null;
        bool many = _board.Ordered.Count > 1 || row == null;
        if (_previous != null) _previous.IsEnabled = many;
        if (_next != null) _next.IsEnabled = many;
        RefreshFix();
    }

    private void MarkRows()
    {
        foreach (var (key, button) in _rowOf) button.Tag = key == _board.Focus ? "on" : null;
        // 그 부재만 보고 있는 이름: 그림의 색(남는 쪽 주황 · 잘리는 쪽 초록) 테두리 — 이름과 그림의 부재를 잇는다.
        foreach (var (key, member, winner, cell) in _cells)
        {
            bool on = key == _board.Focus && member == _solo;
            var (r, g, b) = winner ? WinnerColor : LoserColor;
            cell.Tag = on ? "on" : null;
            cell.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(r, g, b)) : Brushes.Transparent;
        }
    }

    private void BringRowIntoView()
    {
        if (_rowOf.TryGetValue(_board.Focus, out var button)) button.BringIntoView();
    }

    /// <summary>그림 위에 한 줄 알린다.</summary>
    public void ShowMessage(string text, bool warn = false)
    {
        _message.Text = text;
        _message.Foreground = warn ? _ui["WarnFg"] : _ui["Text"];
        _toast.Visibility = Visibility.Visible;
    }

    private void HideMessage() => _toast.Visibility = Visibility.Collapsed;

    private void Note(string what)
    {
        if (Trail.Count < 400) Trail.Add($"{DateTime.Now:HH:mm:ss.fff}\t{what}");
    }

    // Esc: 끌던 것을 그만두고 → 초점이 있으면 전체 보기로 → 없으면 닫는다. (단추에 IsCancel 을 주지 않았다 — 주면 초점이 있어도 한 번에 닫힌다.)
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_busy) { e.Handled = true; return; }      // 고치는 중에는 글쇠를 받지 않는다
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        if (_view3d is { Pressed: true }) { _view3d.CancelDrag(); return; }
        if (_board.Focus >= 0) { Note("Esc → 전체 보기"); ShowAll(); return; }
        Finish(Choice.Close, -1);
    }

    // ── 아래 단추 ─────────────────────────────────────────────────────────────────

    private UIElement Buttons()
    {
        var bar = new DockPanel { LastChildFill = false };

        if (_m.LogPath.Length > 0)
        {
            var open = _ui.Button("결과표 열기", "BtnGhost", "겹친 쌍 전부가 적힌 표(탭으로 나눈 글 — 엑셀로 열 수 있습니다).\n" + _m.LogPath, "openLog", IconOpen);
            open.Click += (_, _) => OpenLog();
            DockPanel.SetDock(open, Dock.Left);
            bar.Children.Add(open);
        }
        else if (_m.LogProblem.Length > 0)
        {
            var problem = new TextBlock { Text = "결과표를 쓰지 못했습니다", Foreground = _ui["Sub"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _ui.Tip(problem, _m.LogProblem);
            DockPanel.SetDock(problem, Dock.Left);
            bar.Children.Add(problem);
        }

        // 넓은 창에서는 Enter · Esc 를 단추에 걸지 않는다: Enter 는 Revit 에서 '마지막 명령 반복'이라 검사가 도는 사이에 한 번 더 누른 Enter 가
        // 창을 닫거나 모델을 바꿀 수 있고, Esc 는 먼저 초점을 푸는 데 쓴다(OnKey).
        void Right(Button button, Choice choice, bool isDefault = false, bool isCancel = false)
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsDefault = isDefault && !_wide;
            button.IsCancel = isCancel && !_wide;
            button.Click += (_, _) => Finish(choice, -1);
            DockPanel.SetDock(button, Dock.Right);
            bar.Children.Add(button);
        }

        // DockPanel 은 오른쪽에 붙이는 순서대로 바깥에서 안으로 놓는다 — 가장 오른쪽 단추를 먼저 넣는다.
        switch (_m.Mode)
        {
            case Mode.Found:
                // 아직 끝나지 않은 쌍만 건다(창에서 고친 쌍은 빼고) + 버림이 잘린 채 결합된 쌍의 순서도 바꾸고 · 결합돼 두 덩이인 벽 · 기둥도 나눈다.
                // 할 것이 없으면 단추를 두지 않는다. 걸 쌍이 없고 나누기만 남았으면 이름표가 그것을 말한다.
                if (Left + _m.LeanToFix + _m.LaterToFix + _m.SplitToDo > 0)
                    Right(_ui.Button(JoinLabel(), "BtnPrimary", JoinTip(), "join"), Choice.Join);
                Right(_ui.Button("겹친 부재 전부 선택", "BtnSecondary",
                    "모델은 바꾸지 않습니다. 겹친 부재들이 Revit 에서 선택된 채로 창이 닫힙니다(창에서 고친 쌍은 빼고).", "selectAll"), Choice.SelectAll);
                Right(_ui.Button("닫기", "BtnGhost", id: "close"), Choice.Close, isCancel: true);
                break;
            case Mode.Joined when Problems > 0:
                Right(_ui.Button($"확인할 부재 선택하고 닫기 ({Problems}쌍)", "BtnPrimary",
                    "결합하지 못했거나 겹침이 남은 부재들이 선택된 채로 창이 닫힙니다.", "selectProblems"), Choice.SelectProblems, isDefault: true);
                Right(_ui.Button("닫기", "BtnGhost", "닫기만 해도 확인할 부재는 선택해 둡니다.", "close"), Choice.Close, isCancel: true);
                break;
            default:
                Right(_ui.Button("닫기", "BtnPrimary", _m.Mode == Mode.Joined ? $"결합한 부재의 판정(DH_판정)은 '{_m.StaleVerdict}'로 바꿔 두었습니다 — [수량산출]의 [완료]가 식을 다시 맞춥니다." : "", "close"),
                    Choice.Close, isDefault: true, isCancel: true);
                // 선택한 부재만 검사했는데 겹침이 없거나 대상이 없다: 전체를 다시 검사할 수 있게 한다.
                if (CanCheckAll && !_wide)
                    Right(_ui.Button("전체 다시 검사", "BtnSecondary", "선택과 상관없이 모델의 콘크리트 부재를 모두 검사합니다(모델은 바꾸지 않습니다).", "checkAll"), Choice.CheckAll);
                // 겹친 쌍은 없지만 순서가 거꾸로 결합된 쌍(버림이 잘림 · 나중 부재가 깎음) · 결합돼 두 덩이인 벽 · 기둥이 있다: 그것만 한다(단추 하나 — 둘 다면 둘 다).
                if (_m.Mode == Mode.Clean && _m.LeanToFix + _m.LaterToFix + _m.SplitToDo > 0)
                    Right(_ui.Button(JoinLabel(), "BtnSecondary", JoinTip(), "join"), Choice.Join);
                break;
        }
        return bar;
    }

    // [우선순위대로 결합] · 작은 창 단추의 이름표 — 넓은 창과 작은 창이 같은 함수로 고른다(R38 core M-2 · L-3).
    // 나누기는 새 부재를 만드는 큰 변경이라 그 수를 이름표에 보인다.
    private string JoinLabel()
    {
        int pairs = _m.Mode == Mode.Found ? Left : 0, lean = _m.LeanToFix, later = _m.LaterToFix, order = lean + later, split = _m.SplitToDo;
        if (pairs > 0) return split > 0 ? $"우선순위대로 결합 ({pairs + order}쌍 · 나누기 {split}개)" : $"우선순위대로 결합 ({pairs + order}쌍)";
        // 순서 바꾸기의 이름: 버림만이면 «버림», 나중 부재만이면 «나중 부재», 둘 다면 그냥 «결합 순서».
        string what = later == 0 ? "버림" : lean == 0 ? "나중 부재" : "";
        if (order > 0 && split > 0) return $"{(what.Length > 0 ? what + " " : "결합 ")}순서 바꾸기 ({order}쌍) · 두 덩이 나누기 ({split}개)";
        return order > 0 ? $"{(what.Length > 0 ? what + " " : "")}결합 순서 바꾸기 ({order}쌍)" : $"두 덩이 벽·기둥 나누기 ({split}개)";
    }

    private string JoinTip()
    {
        var lines = new List<string>();
        if (_m.Mode == Mode.Found && Left > 0)
            lines.Add("남은 쌍 전부에 Revit '형상 결합'을 걸어 우선순위가 낮은 쪽에서 겹친 만큼을 뺍니다.\n우선순위: " + _m.PriorityText);
        if (_m.LeanToFix > 0) lines.Add($"버림이 잘린 채 결합된 쌍 {_m.LeanToFix}개: 버림이 남도록 자르는 순서를 바꿉니다.");
        if (_m.LaterToFix > 0) lines.Add($"나중에 치는 부재가 구조체를 깎은 채 결합된 쌍 {_m.LaterToFix}개: 구조체가 남도록 자르는 순서를 바꿉니다.");
        if (_m.SplitToDo > 0) lines.Add($"결합돼 두 덩이인 벽·기둥 {_m.SplitToDo}개: 덩이마다 나눕니다(위치·부위는 그대로).");
        lines.Add("모델이 바뀝니다(닫은 뒤 Ctrl+Z 한 번으로 되돌림).");
        return string.Join("\n", lines);
    }

    private void Finish(Choice choice, int key)
    {
        Result = choice;
        PickedKey = key;
        Done?.Invoke();
    }

    // 결과표(.tsv)를 연결된 프로그램으로 연다. 연결된 프로그램이 없으면 탐색기에서 그 파일을 짚어 준다.
    private void OpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_m.LogPath) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _m.LogPath + "\"") { UseShellExecute = true }); }
            catch { }
        }
    }

    private static string M3(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    // ── 창 ────────────────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>작은 창(겹침 없음 · 대상 없음)의 너비. 높이는 내용에 맞춘다.</summary>
    public const double WindowWidth = 680;

    /// <summary>
    /// 이 화면을 담은 창. 넓은 창(목록 + 3D)은 화면에 맞춰 크게 띄우고 크기를 바꿀 수 있다 — 가장 작은 크기는 작업 영역 안으로 누른다
    /// (150% 배율의 낮은 화면에서도 아래 단추가 화면 밖으로 나가지 않게). 시험(tools/uipreview)도 이 창에 마우스 신호를 넣는다.
    /// </summary>
    public Window BuildWindow(string title)
    {
        var area = SystemParameters.WorkArea;
        Window window;
        if (_wide)
        {
            double minWidth = Math.Min(1040, area.Width), minHeight = Math.Min(640, area.Height);
            window = new Window
            {
                Title = title, Content = Root, Background = Background,
                Width = Math.Clamp(area.Width - 80, minWidth, 1440), Height = Math.Clamp(area.Height - 60, minHeight, 900),
                MinWidth = minWidth, MinHeight = minHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
            window.PreviewKeyDown += OnKey;
            // 글자판을 그림에 준다: 창을 열자마자 Esc 가 듣는다.
            window.ContentRendered += (_, _) => { if (_view3d is { IsKeyboardFocusWithin: false }) _view3d.Focus(); };
        }
        else
        {
            double maxHeight = Math.Max(420, area.Height - 120);
            window = new Window
            {
                Title = title, Content = Root, Background = Background,
                Width = WindowWidth, SizeToContent = SizeToContent.Height, MaxHeight = Math.Min(maxHeight, 760),
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
        }
        Done += () => window.Close();
        // 고치는 중에는 닫히지 않는다(제목 줄의 X · Alt+F4 — Revit 이 결합하는 동안 창이 사라지면 그 결과를 받을 곳이 없다).
        window.Closing += (_, e) =>
        {
            if (!_busy) return;
            e.Cancel = true;
            Note("고치는 중 — 닫기를 막음");
        };
        // 어두운 테마일 때 창 제목 줄도 어둡게(안 되는 윈도우에서는 그냥 둔다).
        if (_ui.IsDark)
            window.SourceInitialized += (_, _) =>
            {
                try
                {
                    int on = 1;
                    DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
                }
                catch { }
            };
        _window = window;
        return window;
    }

    /// <summary>창으로 띄우고 닫힐 때까지 기다린다. 누른 것은 Result · PickedKey 에 남는다.</summary>
    public void ShowDialog(nint owner, string title)
    {
        var window = BuildWindow(title);
        new WindowInteropHelper(window).Owner = owner;
        // 창이 떠 있는 동안 처리기에서 새는 오류: 그냥 두면 ShowDialog 밖으로 튀어 3D 를 보던 창이 닫힌다 — 창에 적고 머문다.
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler onError = (_, e) =>
        {
            try
            {
                Note("창의 오류\t" + e.Exception);
                // 창 자체는 모델을 바꾸지 않는다. 결합한 뒤의 창이거나 이 창에서 고친 것이 있으면 그 결합은 이미 모델에 걸려 있다 — 그 사실을 그대로 적는다.
                ShowMessage((_m.Mode == Mode.Joined || _changedModel ? "창에서 오류가 났습니다(건 결합은 모델에 그대로 있습니다 — 이 오류로 달라지지 않습니다): "
                                                                     : "창에서 오류가 났습니다(모델은 바뀌지 않았습니다): ") + e.Exception.Message, true);
            }
            catch { }
            e.Handled = true;
        };
        var shown = Stopwatch.StartNew();
        window.ContentRendered += (_, _) =>
            Note($"창이 그려짐\t{shown.ElapsedMilliseconds} ms\t소프트웨어로만 그림 {(RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly ? "O" : "X")}" +
                 $"\t{(_wide ? (_view3d != null ? "목록 + 3D" : "목록만") : "작은 창")}\t그림 등급 {RenderCapability.Tier >> 16}" +
                 // 창과 그림의 크기(픽셀): «끄는 동안 평균 ms»는 그림의 넓이에 따라 달라진다 — 견줄 때 함께 본다.
                 $"\t창 {window.ActualWidth:0}×{window.ActualHeight:0}" + (_view3d != null ? $" · 그림 {_view3d.ActualWidth:0}×{_view3d.ActualHeight:0} · 겉면 {_drawn.Count}(세모 {_drawn.Sum(mesh => mesh.Triangles)})" : "") +
                 (_m.Fix != null ? "\t고치기 있음" : ""));
        EventHandler onFrame = OnFrame;
        if (_view3d != null) CompositionTarget.Rendering += onFrame;
        window.Dispatcher.UnhandledException += onError;
        // 창 안에서 오류가 나도 창이 화면에 남지 않게 한다.
        try { window.ShowDialog(); }
        finally
        {
            window.Dispatcher.UnhandledException -= onError;
            if (_view3d != null) CompositionTarget.Rendering -= onFrame;
            if (_dragFrames > 0)
                Note(string.Create(CultureInfo.InvariantCulture, $"끄는 동안 그림 {_dragFrames}장\t평균 {_dragMs / _dragFrames:0.0} ms\t가장 긴 {_dragWorst:0.0} ms"));
            Note($"닫힘\t{Result}{(PickedKey >= 0 ? " " + PickedKey.ToString(CultureInfo.InvariantCulture) : "")}");
            try { window.Close(); } catch { }
        }
    }

    // 그림을 끄는(돌리기 · 옮기기) 동안 그림 한 장에 걸리는 시간을 잰다 — Revit 은 WPF 를 소프트웨어로만 그려 큰 모델에서 얼마나 무거운지 밖에서는 잴 수 없다.
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_view3d is not { Dragging: true }) { _frameWatch.Reset(); return; }
        if (_frameWatch.IsRunning)
        {
            double ms = _frameWatch.Elapsed.TotalMilliseconds;
            _dragFrames++;
            _dragMs += ms;
            if (ms > _dragWorst) _dragWorst = ms;
        }
        _frameWatch.Restart();
    }
}
