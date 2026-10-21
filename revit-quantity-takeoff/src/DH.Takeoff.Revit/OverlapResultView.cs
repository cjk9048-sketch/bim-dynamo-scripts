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

    /// <summary>
    /// 창에서 누른 것. Pair = [Revit 에서 보기](그 두 부재를 보러 간다) · CheckAll = [전체 다시 검사] · Join = 지금 단계의 [모두 기준대로](Shown 이 단계) ·
    /// Next = [다음 단계](이 창에서 겹침오류를 고쳐 모델이 바뀌었다 — 부르는 쪽이 작성오류를 다시 찾아 ② 로 연다 · R54c).
    /// </summary>
    public enum Choice { Close, Join, SelectAll, SelectProblems, Pair, CheckAll, Next }

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

        /// <summary>이미 결합돼 두 덩이인 벽 · 기둥의 수 — [모두 기준대로]가 함께 나눈다(고칠 것이 이것뿐이면 작은 창의 단추로 · JACK 2026-10-15).</summary>
        public int SplitToDo;

        // ── 결합 방향(R54 모델 검증 B) ──
        /// <summary>
        /// 이미 결합됐는데 순위가 낮은 쪽이 높은 쪽을 자르는 쌍 — 줄의 Winner = 기준대로 남을 쪽(지금 잘림) · Loser = 지금 자르는 쪽 · VolumeM3 = 바꾸면 옮겨질 체적.
        /// Key 는 부르는 쪽의 결합 방향 목록 차례(겹친 쌍의 Key 와 따로). 덩어리는 없다.
        /// </summary>
        public List<OverlapRow> Reversed = new();

        /// <summary>
        /// 결합 방향 줄 하나를 고친다(줄의 Key, 남을 쪽에서 뺄 것인가) — 부르는 쪽(명령)이 넣는다. null 이면 그 단추를 두지 않는다.
        /// 남을 쪽에서 빼기(사용자 모드) = 지금 결합 그대로(기준과 반대)를 «사람이 정함»으로 기억 · 아니면 기준대로 자르는 순서를 바꾼다.
        /// </summary>
        public Func<int, bool, Fixed>? FixReversed;

        /// <summary>
        /// 작성오류 미리보기: 그 줄을 기준대로 바꾸면 두 부재가 될 겉면(그림의 차례 → 겉면 · 줄의 Key) — 부르는 쪽이 시험으로 바꿔 읽고 되돌린다.
        /// 못 만들면 빈 사전. 모델은 바꾸지 않는다.
        /// </summary>
        public Func<int, Dictionary<int, MeshData>>? PreviewReversed;

        /// <summary>반대로 결합됐지만 바꿔도 두 부재 체적이 같아(닿기만 함) 목록에 넣지 않은 쌍의 수.</summary>
        public int Touching;

        /// <summary>이 모델에서 처음 값과 다르게 정한 «겹친 자리» 순위(부위 · 순위 · 처음 순위) — [기준표]에 보인다.</summary>
        public List<(string part, int rank, int first)> RankChanged = new();

        // ── 단계(R54c — JACK 2026-10-21 «탭보다 단계대로 · 겹침오류가 반영된 작성오류») ──
        /// <summary>처음 보일 단계(① 겹침오류 · ② 작성오류). 겹침오류가 없으면 ② 로 연다.</summary>
        public Item Start = Item.Pairs;

        /// <summary>판마다 «결합한 뒤»인가(그 단계의 [모두 기준대로]를 했다 — 다시 찾은 작성오류가 «Pending»으로 보이지 않게). null = Mode 를 따름.</summary>
        public bool? PairsJoined, ReversedJoined;

        /// <summary>창을 열 때 그림 위에 한 줄(다시 찾은 작성오류 등 · "" = 없음) · OpeningWarn = 경고 색(다시 찾지 못함 등).</summary>
        public string Opening = "";
        public bool OpeningWarn;

        /// <summary>② 에서만 보이는 알림(사람이 정한 결합 · 참고 줄 · 재지 못한 작성오류 — ① 에는 다시 찾을 옛 목록의 수라 보이지 않는다).</summary>
        public List<string> ReversedNotes = new();

        /// <summary>
        /// 작성오류 목록이 지금 모델의 것이 아니거나 재지 않은 것이다(겹침오류가 있어 처음에는 재지 않고 찾았다 — 계획 검토 L8): [다음 단계]가 늘 다시 찾게(Next).
        /// </summary>
        public bool ReversedStale;

        /// <summary>작성오류를 다시 찾지 못했다(목록이 빈 것은 «없음»이 아니다 — ② 머리가 경고 · 설치 전 검토 낮음 6).</summary>
        public bool RefindFailed;

        /// <summary>앞의 창(같은 실행)에서 모델이 이미 바뀌었다 — 오류 알림이 «모델은 그대로»라고 하지 않게(계획 검토 L6).</summary>
        public bool ChangedBefore;

        /// <summary>앞의 창에서 고치기를 불러 본 겹침오류(Key) — 다시 연 창에서도 «손댐»(잠금이 되살아나지 않게 · 계획 검토).</summary>
        public HashSet<int> TriedPairs = new();

        // ── 되돌리기(백스페이스 — JACK 2026-10-21 «해당 목록만 초기화») ──
        /// <summary>고른 겹침오류 줄을 고치기 전으로(줄의 Key) — 부르는 쪽이 넣는다. null 이면 백스페이스를 듣지 않는다.</summary>
        public Func<int, Fixed>? Revert;

        /// <summary>고른 작성오류 줄을 고치기 전으로(기준대로 바꾼 줄 = 순서를 되돌림 · 사람이 정함 = 그 기록을 지움).</summary>
        public Func<int, Fixed>? RevertReversed;
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
        public List<OverlapRow> ReversedRows = new();       // 바뀐 결합 방향 줄(R54 — Key = 결합 방향 목록 차례)
        public string Said = "";                            // 잘 됐을 때 알릴 한 줄("" = 없음 — 되돌리기)
    }

    /// <summary>목록이 보여 주는 검사 항목 = 단계(R54c — ① 겹침오류 → ② 작성오류). 화면 이름은 «겹침오류» · «작성오류»(JACK 2026-10-20).</summary>
    public enum Item { Pairs, Reversed }

    // 3D 의 색(JACK 2026-10-14 세 번째: "겹치는 부위는 빨간색, 첫번째 부재는 주황색, 두번째 부재는 녹색 … 고치고 나면 그 부위가 해당하는 부재 색상으로"):
    // 겹친 부위 = 빨강 · 남는 쪽(앞) = 주황 · 잘리는 쪽(뒤) = 초록 · 고친 부위 = 덩어리를 그리지 않아 남는 쪽(결합 뒤 겉면) 안에 들어간 모양(1019 «수정된 상태로») ·
    // 이미 풀린 부위(앞선 결합으로 겹침이 없어짐 — 누구 것인지 모른다) = 회색.
    private static readonly (byte r, byte g, byte b) WinnerColor = (251, 140, 0), LoserColor = (40, 167, 88), BlobColor = (226, 44, 44), LooseColor = (150, 156, 166);
    private const double WideSide = 452;      // 넓은 창의 왼쪽(목록) 처음 너비 — 가르개로 바꾼다
    private const double SideMin = 300, StageMin = 420, SplitterWidth = 12;

    // OverlapChecker.Joined / NotNeeded 와 같은 글자(그쪽은 Revit API 를 쓰는 파일이라 여기서 부르지 않고, 부르는 쪽이 넘긴다).
    private readonly string _joinedText, _notNeededText;
    private readonly UiTheme _ui;
    private readonly Model _m;
    private readonly bool _wide;                             // 넓은 창(목록 + 3D)
    private readonly OverlapBoard _pairs, _reversed;         // 항목마다 판 하나(겹침 · 결합 방향 — core-verify R54 계획 L1)
    private OverlapBoard _board;                             // 지금 목록이 보여 주는 판(항목 띠로 바꾼다 — 그림의 부재 색도 이것을 따른다)
    private Item _item;
    private readonly Dictionary<Item, (Border chip, TextBlock text)> _steps = new();   // 단계 표시(누를 수 없다 — R54c)
    private bool _pairsChanged;                              // 이 창에서 겹침오류를 고쳐 모델이 바뀌었다 — [다음 단계]가 작성오류를 다시 찾게 한다
    private FrameworkElement? _modes;                        // 고치기 줄의 [기본 | 사용자](결합 방향 줄에는 두지 않는다)
    private StackPanel? _legendBlob;                         // 색 풀이의 «겹친 부위»(결합 방향에는 덩어리가 없어 숨긴다)
    private TextBlock? _legendWinner, _legendLoser;          // 색 풀이의 두 부재 이름표(항목마다 목록 칸 이름과 같게)
    private readonly Model3DView? _view3d;
    private readonly int _members;                           // 그림에서 부재의 수(그 뒤가 덩어리다)
    private readonly List<int> _blobKey = new();             // 그림의 덩어리 차례 → 쌍의 Key
    private readonly Dictionary<int, int> _blobAt = new();   // 쌍의 Key → 그림의 차례
    private readonly List<MeshData> _drawn = new();          // 그림에 넘긴 겉면(부재 + 덩어리 — 고친 부재는 새 겉면으로 바꿔 둔다)
    private readonly StackPanel _list = new() { Margin = new Thickness(4) };
    private readonly Grid _header = new();                   // 목록 머리(칸 이름) — 결과 칸이 생기면 다시 짓는다
    private readonly Dictionary<int, Button> _rowOf = new();
    private readonly TextBlock _focusHead = new(), _focusSub = new(), _hover = new(), _message = new();
    private readonly Border _hoverPill = new(), _toast = new();
    private readonly Border _headHost = new(), _buttonsHost = new(), _fixRow = new(), _notesHost = new();
    private readonly StackPanel _fixActions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private Button? _previous, _next, _showAll, _inRevit;
    private RadioButton? _basic, _user;
    private ColumnDefinition? _sideColumn;
    private ScrollViewer? _listScroll;
    private Window? _window;
    private int _preview = -1;                               // 고치기 단추에 마우스를 올린 동안: 겹친 만큼이 빠질 부재(그림의 차례) — 고친 뒤의 모습으로 그린다
    private readonly List<int> _shaped = new();              // 그동안 겉면을 고친 뒤의 모양으로 잠깐 바꿔 둔 부재(그림의 차례 — 작성오류는 둘) — 떼면 _drawn 의 것으로 돌린다
    private readonly Dictionary<(int key, bool cutWinner), (MeshData? mesh, int at)> _cutShapes = new();   // 받아 둔 미리보기 모양(고치면 비운다)
    private bool _reversedPreview;                           // 작성오류 고치기 단추에 마우스를 올린 동안(→ 글쇠도): 두 부재를 바꾼 뒤의 모양으로 그린다
    private readonly Dictionary<int, Dictionary<int, MeshData>> _reversedShapes = new();   // 받아 둔 작성오류 미리보기 모양(줄의 Key → 칸 → 겉면 · 고치면 비운다)
    // 마우스가 올라가 있는 고치기 단추와 그 단추가 지시하는 모습(JACK 2026-10-21 «좌우 방향키로 바꿔 보더라도 마우스가 단추 위면 무조건 그 단추의 뷰») —
    // 방향키 ← → 보다 앞선다. 고치기 줄을 다시 지으면(줄을 옮김 · 고침) 비우고, 새 단추가 마우스 밑에 오면 다시 잡는다(RefreshFix).
    private Button? _hoverButton;
    private Action? _hoverView;
    private bool _enterArmed = true;                         // 엔터가 고치기를 부른 뒤에는 글쇠를 뗄 때까지 받지 않는다
    private bool? _keyView;                                  // 고른 줄에서 방향키로 본 모습(→ = 고친 뒤 · ← = 지금 · null = 아직) — 엔터가 그 모습대로 고친다(JACK 2026-10-21 «단축키처럼»)
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

    /// <summary>[Revit 에서 보기]로 고른 것이 결합 방향 줄이다(PickedKey = 결합 방향 목록 차례).</summary>
    public bool PickedReversed { get; private set; }

    /// <summary>넓은 창(목록 + 3D)인가.</summary>
    public bool Wide => _wide;

    /// <summary>3D 그림(없으면 null — 작은 창이거나 3D 를 만들지 못했다).</summary>
    public Model3DView? View3D => _view3d;

    /// <summary>창의 상태(초점 · 묶음 — 지금 보이는 항목의 판) — 시험에서 본다.</summary>
    public OverlapBoard Board => _board;

    /// <summary>겹침 · 결합 방향 판(시험에서 본다).</summary>
    public OverlapBoard PairsBoard => _pairs;
    public OverlapBoard ReversedBoard => _reversed;

    /// <summary>목록이 보여 주는 항목.</summary>
    public Item Shown => _item;

    /// <summary>이 창에서 한 일(일지에 옮겨 적는다): 누른 줄 · 찍은 덩어리 · 초점이 어디로 갔는지 · 고친 쌍 · 그리는 데 걸린 시간.</summary>
    public List<string> Trail { get; } = new();

    // 시험에서 본다: 마우스 밑의 것을 적은 글 · 초점 띠의 글 · 그림 위의 알림(보이지 않으면 "") · 목록의 폭 · 고치기 줄이 보이는가.
    public string HoverText => _hoverPill.Visibility == Visibility.Visible ? _hover.Text : "";
    public string FocusText => string.Concat(_focusHead.Inlines.OfType<Run>().Select(run => run.Text)) + " | " + _focusSub.Text;
    public string MessageText => _toast.Visibility == Visibility.Visible ? _message.Text : "";
    public double SideWidth => _sideColumn?.ActualWidth ?? 0;
    public bool FixShown => (_m.Fix != null || _m.FixReversed != null) && _fixRow.Visibility == Visibility.Visible;

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
        _triedPairs.UnionWith(model.TriedPairs);
        _changedModel = model.ChangedBefore;
        _pairs = new OverlapBoard(model.Rows, model.PairsJoined ?? model.Mode == Mode.Joined);
        _reversed = new OverlapBoard(model.Reversed, model.ReversedJoined ?? model.Mode == Mode.Joined);
        // 처음 단계: 부르는 쪽이 정한 것(겹침오류가 없으면 ②). 작성오류만 걸린 모델도 ② 다.
        _item = model.Rows.Count == 0 ? Item.Reversed : model.Start;
        _board = _item == Item.Pairs ? _pairs : _reversed;
        // 넓은 창: 어느 항목이든 줄이 있으면(revit-verify R54 계획 H1 — 결합 방향만 걸린 모델이 작은 창으로 빠지지 않게).
        _wide = (model.Rows.Count > 0 || model.Reversed.Count > 0) && model.Mode is Mode.Found or Mode.Joined;

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
            if (_m.Notes.Count + _m.ReversedNotes.Count > 0) Top(dock, NotesBanner());
            dock.Children.Add(new Border());
            return;
        }

        // ── 넓은 창: 머리(결과 + 숫자 칸) · 알림 · [왼쪽 목록 | 가르개 | 오른쪽 초점 띠 + 3D] · 단추 ──
        _headHost.Margin = new Thickness(0, 0, 0, 12);
        Top(dock, _headHost);
        if (_m.SelectionOnly) Top(dock, SelectionBanner());
        Top(dock, _notesHost);                            // 알림 띠는 단계마다 다시 짓는다(RefreshChrome — ② 의 알림은 ② 에서만)
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
        root.PreviewKeyUp += (_, e) => { if (e.Key == Key.Enter) _enterArmed = true; };   // 엔터는 뗀 뒤에 다시 받는다(계획 검토 중간 2)
        root.PreviewMouseLeftButtonDown += OnPressAnything;
        RefreshChrome();
        RefreshList();
        RefreshFocus();
        if (_m.Opening.Length > 0) ShowMessage(_m.Opening, _m.OpeningWarn);
    }

    private static void Top(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Top); dock.Children.Add(child); }
    private static void Bottom(DockPanel dock, UIElement child) { DockPanel.SetDock(child, Dock.Bottom); dock.Children.Add(child); }

    // 확인할 쌍: 결합 못 함 · 겹침이 남음. 결합한 뒤의 창은 결합하지 않은 쌍(다시 찾아 나온 새 쌍 · 다시 연 쌍)도 — 보드의 «끝나지 않은 쌍»과 같다
    // (core-verify R50 높음: 그것을 빼면 새 겹침이 남았는데 «모두 정리»가 뜬다).
    // 결합 방향은 결합한 뒤의 창에서 못 바꾼 줄을 더한다(R54).
    // 단계마다 결합한 뒤의 판만 센다(R54c — PairsJoined · ReversedJoined). 결합하기 전의 겹침오류는 창에서 고쳐 본 결과(못 함 · 겹침 남음)를 센다.
    private int Problems => PairsProblems + _reversed.Problems;
    private int PairsProblems => _pairs.Joined ? _pairs.Problems : _m.StillOverlap + _m.Failed;

    // 쌍의 수는 보드에서 읽는다(고친 뒤 다시 찾아 붙인 줄까지 — core-verify R50 낮음 8).
    private int Pairs => _pairs.Ordered.Count;

    // 결합한 뒤의 창에서 아직 결합하지 않은 쌍(다시 찾아 나온 새 쌍 · 다시 연 쌍).
    private int Pending => _pairs.Ordered.Count(r => !r.Fine && r.Status.Length == 0);

    // [모두 기준대로]가 거는 쌍(고치지 않았거나 · 못 했거나 · 겹침이 남은 쌍 — 여러 조각과 겹쳐 다시 검사할 쌍은 빼고).
    private int Left => _pairs.Joinable;

    // 결합 방향: 수(사람이 정함 · 참고는 빼고) · [모두 기준대로]가 바꾸는 줄(Revit 이 거절한 줄 · 바꾼 줄은 빼고).
    private int ReversedCount => _reversed.Counted;
    private int ReversedLeft => _reversed.Joinable;
    private double ReversedM3 => _reversed.Ordered.Where(r => !r.Optional && !r.Unmeasured).Sum(r => r.VolumeM3);

    // 결과가 하나라도 있다(결합한 뒤의 창이거나, 창에서 하나라도 고쳐 봤다).
    private bool Touched => _pairs.Joined || _m.Joined + _m.NotNeeded + _m.StillOverlap + _m.Failed > 0;

    // [전체 다시 검사]를 둘 것인가: 결합한 뒤의 창에는 두지 않는다 — 부르는 쪽은 그 뒤로는 처음부터 다시 돌지 않는다.
    private bool CanCheckAll => _m.CanCheckAll && _m.Mode != Mode.Joined;

    // ── 머리: 한눈에 보이는 결과 ──────────────────────────────────────────────────

    // sub = 제목 밑의 한 줄(작은 창에만 — 넓은 창에는 두지 않는다).
    private UIElement Head(bool margin = true)
    {
        (string tone, string glyph, string title, string sub) = _m.Mode switch
        {
            // 넓은 창은 지금 단계의 결과(R54c): ① 겹침오류 · ② 작성오류 — 결합하기 전(찾음) · 결합한 뒤(정리 · 확인할 것).
            Mode.Found or Mode.Joined when _item == Item.Pairs => _pairs.Joined
                ? PairsProblems == 0 ? ("Good", UiTheme.IconCheck, "겹침오류를 모두 기준대로 정리했습니다", "") : ("Warn", UiTheme.IconWarn, $"겹침오류 {PairsProblems}쌍은 직접 확인해 주세요", "")
                : ("Warn", UiTheme.IconWarn, $"겹침오류 {Pairs}쌍을 찾았습니다", ""),
            Mode.Found or Mode.Joined when _m.RefindFailed && !_reversed.Joined => ("Warn", UiTheme.IconWarn, "작성오류를 다시 찾지 못했습니다", ""),
            Mode.Found or Mode.Joined => _reversed.Joined
                ? _reversed.Problems == 0 ? ("Good", UiTheme.IconCheck, "작성오류를 모두 기준대로 바꿨습니다", "") : ("Warn", UiTheme.IconWarn, $"작성오류 {_reversed.Problems}쌍은 직접 확인해 주세요", "")
                : ReversedCount > 0 ? ("Warn", UiTheme.IconWarn, $"작성오류 {ReversedCount}쌍을 찾았습니다", "") : ("Good", UiTheme.IconCheck, "작성오류가 없습니다", ""),
            Mode.Clean => ("Good", UiTheme.IconCheck, "모델 검증 통과",
                _m.SelectionOnly ? "선택한 부재끼리는 겹친 자리도 기준과 반대인 결합도 없습니다."
                : _m.SplitToDo > 0 ? "겹친 자리도 기준과 반대인 결합도 없습니다."      // 두 덩이 나누기가 먼저다 — «바로 산출식»은 권하지 않는다
                : "겹친 자리도 기준과 반대인 결합도 없습니다. 이대로 [수량산출]을 하면 됩니다."),
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
        _notesHost.Child = NotesBanner();
        _buttonsHost.Child = Buttons();
    }

    // ── 숫자 칸 ───────────────────────────────────────────────────────────────────

    // compact = 넓은 창의 머리 오른쪽에 나란히 놓는 좁은 칸.
    private FrameworkElement? Tiles(bool compact)
    {
        var tiles = new List<Border>();
        // 넓은 창은 지금 단계의 칸만(R54c): ① 은 결합하기 전 꼴(Found) · 결합한 뒤 꼴(Joined)을 판으로 가른다 · ② 도.
        var mode = _m.Mode is Mode.Found or Mode.Joined
            ? _item == Item.Pairs ? (_pairs.Joined ? Mode.Joined : Mode.Found) : (_reversed.Joined ? Mode.Joined : Mode.Found)
            : _m.Mode;
        bool pairsStep = _item == Item.Pairs, reversedStep = _item == Item.Reversed;
        switch (mode)
        {
            case Mode.Found:
                if (pairsStep)
                {
                    tiles.Add(Tile("겹침오류", Pairs.ToString(CultureInfo.InvariantCulture), "쌍", "WarnFg", "결합 없이 겹쳐 두 번 세는 쌍"));
                    if (Touched)
                    {
                        // 창에서 고쳐 본 뒤: 정리된 쌍 · (확인할 쌍) · 남은 중복 체적.
                        tiles.Add(Tile("정리됨", (_m.Joined + _m.NotNeeded).ToString(CultureInfo.InvariantCulture), "쌍", "GoodFg"));
                        if (Problems > 0) tiles.Add(Tile("확인 필요", Problems.ToString(CultureInfo.InvariantCulture), "쌍", "WarnFg", "결합하지 못했거나 결합한 뒤에도 겹침이 남은 쌍"));
                        tiles.Add(Tile("남은 중복 체적", M3(_m.RemainingM3), "m³", _m.RemainingM3 >= 0.005 ? "WarnFg" : "GoodFg", $"처음 찾았을 때: {M3(_m.TotalM3)} m³"));
                    }
                    else tiles.Add(Tile("두 번 세는 체적", M3(_m.TotalM3), "m³", "WarnFg"));
                }
                // 작성오류(R54 결합 방향): 남은 줄 수 · 바꾸면 옮겨질 체적의 합.
                if (reversedStep)
                    tiles.Add(Tile("작성오류", ReversedLeft.ToString(CultureInfo.InvariantCulture), "쌍", ReversedLeft > 0 ? "WarnFg" : "GoodFg",
                        $"기준과 반대로 결합된 쌍 {ReversedCount}개 · 바꾸면 옮겨질 체적 {M3(ReversedM3)} m³(잰 것만)"));
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                break;
            case Mode.Joined:
                if (pairsStep)
                {
                    tiles.Add(Tile("결합", (_m.Joined + _m.NotNeeded).ToString(CultureInfo.InvariantCulture), "쌍", "GoodFg",
                        _m.NotNeeded > 0 ? $"이 가운데 {_m.NotNeeded}쌍은 앞선 결합으로 겹침이 이미 없어져 따로 걸지 않았습니다." : ""));
                    // 결합하지 않은 쌍(다시 찾아 나온 새 겹침)도 겹침이 남은 쌍으로 센다.
                    tiles.Add(Tile("겹침이 남음", (_m.StillOverlap + Pending).ToString(CultureInfo.InvariantCulture), "쌍", _m.StillOverlap + Pending > 0 ? "WarnFg" : "Sub",
                        Pending > 0 ? $"이 가운데 {Pending}쌍은 다시 찾아 나온 새 겹침으로 아직 결합하지 않았습니다." : ""));
                    tiles.Add(Tile("결합 못 함", _m.Failed.ToString(CultureInfo.InvariantCulture), "쌍", _m.Failed > 0 ? "BadFg" : "Sub"));
                    tiles.Add(Tile("남은 중복 체적", M3(_m.RemainingM3), "m³", _m.RemainingM3 >= 0.005 ? "WarnFg" : "GoodFg",
                        $"결합하기 전: {M3(_m.TotalM3)} m³"));
                }
                if (reversedStep)
                {
                    int switched = _reversed.Ordered.Count(r => r.Fine && !r.Optional);
                    tiles.Add(Tile("작성오류 고침", switched.ToString(CultureInfo.InvariantCulture), "쌍", switched == ReversedCount ? "GoodFg" : "WarnFg",
                        $"작성오류(기준과 반대로 결합된 쌍) {ReversedCount}개 가운데 기준대로 바꾼 쌍"));
                }
                break;
            case Mode.Clean:
                tiles.Add(Tile(_m.SelectionOnly ? "검사한 부재(선택)" : "검사한 부재", _m.Checked.ToString(CultureInfo.InvariantCulture), "개", "Text"));
                tiles.Add(Tile("겹침오류", "0", "쌍", "GoodFg", "결합 없이 겹쳐 두 번 세는 쌍"));
                tiles.Add(Tile("작성오류", "0", "쌍", "GoodFg", "기준과 반대로 결합된 쌍"));
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
        var notes = _m.Notes.Concat(!_wide || _item == Item.Reversed ? _m.ReversedNotes : Enumerable.Empty<string>()).ToList();
        if (notes.Count == 0) return new Border();
        foreach (string note in notes)
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
        // 단계 표시(R54c — JACK 2026-10-21 «탭으로 분리하는 것보다 단계대로»): «① 겹침오류 n › ② 작성오류 m» — 누를 수 없다(넘어가기는 아래 [다음 단계] ·
        // [이전 단계]) · 지금 단계 강조 · 오른쪽 [기준표]. 수 · 잠금은 RefreshItems 가 적는다.
        var bar = new DockPanel { Margin = new Thickness(10, 9, 10, 8) };
        var table = _ui.Button("기준표", "BtnGhost", "겹친 자리를 누가 가지는지(겹친 자리 순위)의 기준 — 바꾸는 곳은 [수량산출] ① 부위 목록의 «겹친 자리»입니다.", "rankTable");
        table.MinHeight = 28;
        table.Padding = new Thickness(10, 0, 10, 1);
        table.Focusable = false;
        table.Click += (_, _) => { Note("기준표"); ShowRankTable(); };
        DockPanel.SetDock(table, Dock.Right);
        bar.Children.Add(table);
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in new[] { Item.Pairs, Item.Reversed })
        {
            var text = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 4, 12, 5), Child = text };
            _ui.Mark(chip, item == Item.Pairs ? PairsItemTip : ReversedItemTip, item == Item.Pairs ? "step.pairs" : "step.reversed");
            _steps[item] = (chip, text);
            if (item == Item.Reversed)
                strip.Children.Add(new TextBlock { Text = "›", Foreground = _ui["Sub"], FontSize = 15, Margin = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Center });
            strip.Children.Add(chip);
        }
        bar.Children.Add(strip);
        _header.Margin = new Thickness(15, 0, 22, 6);
        var top = new StackPanel();
        top.Children.Add(bar);
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
    private bool ShowStatus => _board.Joined || _board.Ordered.Any(r => r.Status.Length > 0 || r.New || r.Note.Length > 0);

    private const string PairsItemTip = "겹침오류: 결합 없이 겹쳐 두 번 세는 자리 — 고르면 결합해서 순위가 낮은 쪽에서 뺍니다.";
    private const string ReversedItemTip = "작성오류: 이미 결합됐는데 순위가 낮은 쪽이 높은 쪽을 자르는 쌍 — 겹친 자리가 기준과 반대 부재에서 빠져 있습니다.";

    // 작성오류는 겹침오류를 다 고친 뒤에 연다(JACK 2026-10-20 «겹침오류를 해결해야만 작성오류로» · 고름 «고칠 수 있는 건 다 고치면 열림»):
    // 아직 손대지 않은 겹침오류(이 창에서 고칠 수 있는데 결과가 없는 쌍)가 남으면 잠근다. 고쳐 봤지만 못 한 쌍(Failed · Overlap) · 이 창에서 고칠 수 없는 쌍
    // (Blocked) · 보이기만 하는 줄은 막지 않는다(막다른 길 없음). 창에서 고칠 수 없으면(묶음을 못 열어 고치기 단추가 없음) 잠그지 않는다 — 보기만 한다.
    // 이 창에서 고치기를 불러 본 쌍도 «손댐»이다 — 부르는 쪽이 결과 글을 못 적고 던진 쌍이 작성오류를 끝내 잠그지 않게(설치 전 검토 낮음 1).
    private int PairsUntried => _pairs.Ordered.Count(r => !r.Fine && !r.Blocked && !r.Optional && r.Status.Length == 0 && !_triedPairs.Contains(r.Key));
    private readonly HashSet<int> _triedPairs = new();
    private bool ReversedLocked => _m.Fix != null && PairsUntried > 0;

    /// <summary>작성오류 항목이 잠겨 있다(겹침오류에 아직 고치지 않은 쌍이 남음) — 시험에서 본다.</summary>
    public bool ReversedLockedNow => ReversedLocked;

    /// <summary>이 창(과 앞의 창)에서 고치기를 불러 본 겹침오류 — 부르는 쪽이 다음 창에 넘긴다.</summary>
    public IReadOnlyCollection<int> TriedPairs => _triedPairs;

    // 단계 표시의 글: «① 겹침오류 n» · «② 작성오류 m»(사람이 정함 · 참고는 빼고). ② 의 수는 ② 에서만 — ① 에서는 넘어갈 때 다시 찾는다(겹침오류를 고친 결과로).
    // ① 을 지나 ② 에 있으면 ① 에 ✓ · 지금 단계는 강조. 작성오류가 잠겼으면 ② 의 풍선 글이 까닭(넘어가는 단추도 꺼진다 — StepButtons).
    private void RefreshItems()
    {
        bool locked = ReversedLocked;
        foreach (var (item, (chip, text)) in _steps)
        {
            bool now = item == _item;
            text.Text = item == Item.Pairs
                ? "① 겹침오류 " + Pairs.ToString(CultureInfo.InvariantCulture) + (_item == Item.Reversed ? "  ✓" : "")
                : "② 작성오류" + (_item == Item.Reversed ? " " + ReversedCount.ToString(CultureInfo.InvariantCulture) : "");
            text.Foreground = now ? _ui["AccentText"] : _ui["Sub"];
            text.FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal;
            chip.Background = now ? _ui["AccentSoft"] : Brushes.Transparent;
            if (item == Item.Reversed) _ui.Tip(chip, locked ? $"겹침오류를 먼저 고쳐 주세요 — 아직 고치지 않은 쌍 {PairsUntried}개" : ReversedItemTip);
        }
    }

    /// <summary>단계 표시의 글(시험에서 본다): «① 겹침오류 12 | ② 작성오류».</summary>
    public string StepsText => string.Join(" | ", _steps.Values.Select(s => s.text.Text));

    /// <summary>
    /// 단계를 바꾼다(창 안에서 — [다음 단계]가 다시 찾을 것이 없을 때 · [이전 단계]) — 그림의 부재 색 · 초점 띠 · 고치기 줄 · 머리 · 숫자 칸 · 아래 단추도 그 단계의 것.
    /// 작성오류가 잠겼으면 그대로 둔다.
    /// </summary>
    public void ShowItem(Item item)
    {
        if (_item == item && ReferenceEquals(_board, item == Item.Pairs ? _pairs : _reversed)) return;
        if (item == Item.Reversed && ReversedLocked)
        {
            Note($"단계 작성오류 — 잠김(겹침오류 남음 {PairsUntried})");
            RefreshItems();
            return;
        }
        _item = item;
        _board = item == Item.Pairs ? _pairs : _reversed;
        _stepChanged.Restart();
        Note($"단계 {(item == Item.Pairs ? "① 겹침오류" : "② 작성오류")}");
        RefreshChrome();
        RefreshList();
        RefreshFocus();
        Frame();
    }

    // 단계를 바꾼 직후(두 번 누름의 사이) — 그 자리에 새로 놓인 아래 단추가 두 번째 누름을 받지 않게(계획 검토 높음 1: [다음 단계]를 두 번 누르면 ② 의 [모두 기준대로]).
    private readonly Stopwatch _stepChanged = new();
    private bool StepJustChanged => _stepChanged.IsRunning && _stepChanged.ElapsedMilliseconds < Math.Max(300, GetDoubleClickTime());

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    // [다음 단계: 작성오류](R54c — JACK 2026-10-21 «겹침오류를 수행하고 작성오류 · 겹침오류가 반영된 작성오류»): 잠겼으면 아무것도 안 함(단추가 꺼져 있다).
    // 이 창에서 겹침오류를 고쳐 모델이 바뀌었으면 창을 닫는다(Next) — 부르는 쪽이 지금 모델로 작성오류를 다시 찾아 ② 로 연다. 안 바뀌었으면 목록이 이미 지금 모델의 것이라 창 안에서.
    private void GoNext()
    {
        if (_busy || ReversedLocked || _item == Item.Reversed) return;
        if (StepJustChanged) return;
        if ((_pairsChanged || _m.ReversedStale) && _m.Fix != null)
        {
            Note($"다음 단계 — 작성오류를 다시 찾음({(_pairsChanged ? "겹침오류를 고침" : "처음 목록은 재지 않음")})");
            Finish(Choice.Next, -1);
            return;
        }
        ShowItem(Item.Reversed);
    }

    // 목록을 다시 짓는다(묶음을 접거나 폈을 때 · 고쳤을 때 · 항목을 바꿨을 때 · 처음).
    private void RefreshList()
    {
        bool status = ShowStatus;
        bool joined = _pairs.Joined;
        RefreshItems();
        if (_legendWinner != null && _legendLoser != null && _legendBlob != null)
        {
            _legendWinner.Text = _item == Item.Pairs ? "남는 쪽" : "남을 쪽";
            _legendLoser.Text = _item == Item.Pairs ? "잘리는 쪽" : "잘릴 쪽";
            _legendBlob.Visibility = _item == Item.Pairs ? Visibility.Visible : Visibility.Collapsed;
        }
        _header.Children.Clear();
        SetColumns(_header, status);
        if (_item == Item.Pairs)
        {
            HeaderCell(_header, 0, "겹친 체적", HorizontalAlignment.Right);
            HeaderCell(_header, 2, "남는 쪽");
            HeaderCell(_header, 4, joined ? "잘린 쪽" : "잘리는 쪽");
        }
        else
        {
            // 결합 방향: 기준대로 바꾸면 «남을 쪽»이 그 자리를 얻고 «잘릴 쪽»(지금 자르는 쪽)에서 빠진다.
            HeaderCell(_header, 0, "바뀔 체적", HorizontalAlignment.Right);
            HeaderCell(_header, 2, "남을 쪽");
            HeaderCell(_header, 4, "잘릴 쪽(지금 자름)");
        }
        if (status) HeaderCell(_header, 6, "결과");
        if (_board.Ordered.Count == 0)
        {
            _list.Children.Clear();
            _rowOf.Clear();
            _cells.Clear();
            _list.Children.Add(new TextBlock
            {
                Text = _item == Item.Pairs ? "겹침오류가 없습니다." : "작성오류가 없습니다.", Foreground = _ui["Sub"], Margin = new Thickness(12, 10, 12, 10),
            });
            return;
        }

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
        count.Inlines.Add(new Run($"{group.Counted}쌍 · {M3(group.CountedM3)} m³"));   // 보이기만 하는 줄(사람이 정함 · 참고)은 세지 않는다(R54)
        var line = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
        DockPanel.SetDock(count, Dock.Right);
        line.Children.Add(count);
        if (group.Problems > 0)
        {
            var warn = _ui.Pill(new TextBlock { Text = $"Check {group.Problems}", FontSize = 11.5, Foreground = _ui["WarnFg"] }, "WarnBg");
            _ui.Tip(warn, $"확인할 쌍 {group.Problems}개(결합 못 함 · 겹침 남음 · 못 바꿈)");
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(withStatus ? 92 : 0) });   // 가장 긴 딱지 «Unmeasured»
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
        if (row.Unmeasured) volume.Inlines.Add(new Run("—") { Foreground = _ui["Sub"] });
        else
        {
            volume.Inlines.Add(new Run(row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)) { FontWeight = FontWeights.SemiBold });
            volume.Inlines.Add(new Run(" m³") { FontSize = 11, Foreground = _ui["Sub"] });
        }
        grid.Children.Add(volume);

        Place(grid, 2, MemberCell(row.Key, row.Winner, row.WinnerIndex, winner: true));
        var arrow = UiTheme.Icon(IconCut, 11);
        arrow.Foreground = _ui["Sub"];
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        Place(grid, 3, arrow);
        Place(grid, 4, MemberCell(row.Key, row.Loser, row.LoserIndex, winner: false));
        if (_item == Item.Reversed)
        {
            if (withStatus && ReversedPill(row) is { } pill) Place(grid, 6, pill);
        }
        else if (withStatus && row.Status.Length > 0) Place(grid, 6, StatusPill(row));
        else if (withStatus && row.New) Place(grid, 6, NewPill());
        else if (withStatus && _pairs.Joined) Place(grid, 6, PendingPill());   // 결합한 뒤 창에서 다시 연 쌍(core-verify R50 설치 전 B)

        var button = new Button { Style = _ui.StyleOf("RowPick"), Content = grid };
        string tip = _item == Item.Pairs
            ? $"{row.Winner}  →  {row.Loser}\n겹친 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³"
            : $"지금은 {row.Loser} 이 {row.Winner} 을 자릅니다.\n기준대로 바꾸면 " +
              (row.Unmeasured ? "겹친 자리가" : $"{row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³ 가") + $" {Split(row.Loser).main} 에서 빠집니다." +
              (row.Note.Length > 0 ? "\n" + row.Note : "");
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
            // 딱지 글은 짧은 영단어(JACK 2026-10-20 «프로그램처럼») — 자세한 까닭은 풍선 글(원래 글 그대로).
            row.Status == _joinedText ? ("Joined", "Good")
            : row.Status == _notNeededText ? ("Resolved", "Neutral")
            : row.Status.StartsWith("결합 못 함", StringComparison.Ordinal) ? ("Failed", "Bad")
            : row.Status.Length == 0 ? ("—", "Neutral")
            : row.Status.StartsWith(_joinedText + "(", StringComparison.Ordinal) ? ("Check", "Warn") // 결합은 됐지만 다시 재지 못했다
            : ("Overlap", "Warn");
        // 결합으로 두 덩이가 된 잘린 쪽을 나눴는가(JACK 2026-10-14): 나눴으면 «결합·나눔», 못 나눴으면 «못 나눔»(까닭은 풍선 글).
        if (row.Status == _joinedText && row.Split.Length > 0)
            (label, tone) = row.Split.StartsWith(OverlapRow.SplitFail, StringComparison.Ordinal) ? ("No Split", "Warn") : ("Split", "Good");
        var pill = _ui.Pill(new TextBlock { Text = label, FontSize = 12, Foreground = _ui[tone + "Fg"] }, tone + "Bg");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        string detail = row.Status + (row.Split.Length > 0 ? "\n" + row.Split : "");
        if (!double.IsNaN(row.AfterM3) && row.AfterM3 >= OverlapShownM3)
            detail += $"\n결합한 뒤 남은 겹침: {row.AfterM3.ToString("0.000", CultureInfo.InvariantCulture)} m³";
        string meaning = label switch
        {
            "Joined" => "결합됨", "Resolved" => "이미 풀림(앞선 결합으로 겹침이 없어짐)", "Failed" => "결합 못 함", "Check" => "확인 필요(결합은 됐지만 다시 재지 못함)",
            "Overlap" => "겹침 남음", "Split" => "결합·나눔", "No Split" => "못 나눔", _ => "",
        };
        _ui.Tip(pill, string.Join("\n", new[] { meaning, detail }.Where(s => s.Length > 0)));
        return pill;
    }

    // 결합 방향 줄의 딱지(R54): 바꿈 · 못 바꿈(Revit 이 거절) · 사람이 정함 · 참고 · 안 잼. 없으면 null.
    private Border? ReversedPill(OverlapRow row)
    {
        (string label, string tone) =
            // 짧은 영단어(JACK 2026-10-20 «프로그램처럼»): Changed 바꿈 · Failed 못 바꿈 · Manual 사람이 정함 · Info 참고(같은 순위) · Unmeasured 안 잼 · Pending 아직 안 함.
            row.Fine ? ("Changed", "Good")
            : row.Blocked || row.Status.StartsWith("못 바꿈", StringComparison.Ordinal) ? ("Failed", "Bad")
            : row.Optional ? (row.Note.StartsWith(HumanNote, StringComparison.Ordinal) ? "Manual" : "Info", "Neutral")
            : row.Unmeasured ? ("Unmeasured", "Neutral")
            : _reversed.Joined ? ("Pending", "Warn")
            : ("", "");
        if (label.Length == 0) return null;
        var pill = _ui.Pill(new TextBlock { Text = label, FontSize = 12, Foreground = _ui[tone + "Fg"], TextTrimming = TextTrimming.CharacterEllipsis }, tone + "Bg");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        // 풍선 글: 딱지의 우리말 뜻 + 자세한 까닭.
        string meaning = label switch
        {
            "Changed" => "기준대로 바꿈",
            "Failed" => "못 바꿈 — Revit 이 자르는 순서를 바꾸지 못하게 함",
            "Manual" => "사람이 정함 — 기준과 달라도 그대로 둠(수 · [모두 기준대로]에서 빠짐 · 하나씩은 바꿀 수 있음)",
            "Info" => "참고 — 같은 순위라 기준표가 정하지 않는 쌍(수 · [모두 기준대로]에서 빠짐)",
            "Unmeasured" => "바뀔 체적을 재지 못함(바꾸기는 됨)",
            _ => "아직 안 함",
        };
        _ui.Tip(pill, string.Join("\n", new[] { meaning, row.Status, row.Note }.Where(s => s.Length > 0).Distinct()));
        return pill;
    }

    /// <summary>결합 방향 줄의 Note 가 이 글로 시작하면 사람이 정한 결합이다(부르는 쪽이 같은 글을 쓴다 — 딱지 «Manual»).</summary>
    public const string HumanNote = "사람이 정함";

    // 고친 뒤 다시 찾아 새로 나온 줄의 딱지(결과가 생기면 결과 딱지가 대신한다 — JACK 2026-10-19).
    private Border NewPill()
    {
        var pill = _ui.Pill(new TextBlock { Text = "New", FontSize = 12, Foreground = _ui["AccentText"] }, "AccentSoft");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        _ui.Tip(pill, "앞의 쌍을 고친 뒤 다시 찾아 새로 나온 겹침입니다.");
        return pill;
    }

    // 결합한 뒤의 창에서 아직 결합하지 않은 옛 줄(다시 찾기가 다시 연 쌍 — 겹침이 다시 생김)의 딱지.
    private Border PendingPill()
    {
        var pill = _ui.Pill(new TextBlock { Text = "Pending", FontSize = 12, Foreground = _ui["WarnFg"] }, "WarnBg");
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
        _previous = Small("이전", "목록의 앞 쌍으로 갑니다(↑).", "focus.previous", () => Step(-1), icon: IconLeft);
        _next = Small("다음", "목록의 다음 쌍으로 갑니다(↓).", "focus.next", () => Step(+1), icon: IconRight);
        _showAll = Small("전체 보기", "겹친 자리를 모두 봅니다(Esc).", "focus.all", () => { Note("전체 보기"); ShowAll(); });
        _inRevit = Small("Revit 에서 보기", "이 창을 닫고, 지금 보고 있는 쌍의 두 부재를 Revit 에서 선택해 화면에 맞춥니다.", "focus.inRevit", () =>
        {
            if (_board.Focus < 0) return;
            Note($"Revit 에서 보기 {(_item == Item.Reversed ? "작성오류 " : "")}{_board.Focus}");
            PickedReversed = _item == Item.Reversed;
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
        if (_m.Fix != null || _m.FixReversed != null) stack.Children.Add(BuildFixRow());
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
        _basic = _ui.Segment("기본", "기준표대로 고칩니다: " + _m.PriorityText, "fix.mode.basic");
        _user = _ui.Segment("사용자", "겹친 만큼을 어느 부재에서 뺄지 고릅니다.", "fix.mode.user");
        _basic.Focusable = _user.Focusable = false;
        _basic.IsChecked = !s_userMode;
        _user.IsChecked = s_userMode;
        _basic.Checked += (_, _) => SetUserMode(false);
        _user.Checked += (_, _) => SetUserMode(true);
        var modes = _ui.Segments(_basic, _user);
        modes.Margin = new Thickness(0, 0, 10, 0);
        _modes = modes;
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
        if (_m.Fix == null && _m.FixReversed == null) return;
        SetPreview(-1);
        SetReversedPreview(false);
        _hoverButton = null;                              // 옛 단추는 치운다 — 마우스 밑에 새 단추가 오면 배치 뒤에 다시 잡는다(아래 Synchronize)
        _hoverView = null;
        _keyView = null;                                  // 방향키로 본 모습은 그 줄 · 그 단추들의 것이다(줄을 옮기거나 고치면 다시 골라야 엔터가 고친다)
        _hoverTimer?.Stop();
        _fixBuilt.Restart();
        try { _fixBuiltAt = Mouse.GetPosition(Root); } catch { _fixBuiltAt = new Point(double.NaN, double.NaN); }
        _fixActions.Children.Clear();
        // 마우스를 움직이지 않으면 WPF 는 새로 놓인 단추에 «올라감»을 알리지 않는다 — 배치가 끝난 뒤 한 번 맞춰 본다(↑ ↓ 로 줄을 옮겨도
        // 마우스가 단추 자리에 있으면 그 줄의 단추가 지시하는 모습 — JACK 2026-10-21 «무조건»).
        Root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            try { if (!_busy && _window is { IsActive: true }) Mouse.Synchronize(); }
            catch { }
        }));
        var row = _board.Focused;
        bool can = _item == Item.Pairs ? _m.Fix != null : _m.FixReversed != null;
        _fixRow.Visibility = row == null || !can ? Visibility.Collapsed : Visibility.Visible;
        if (row == null || !can) return;
        if (_item == Item.Reversed)
        {
            // 결합 방향(R54): 기본 = [기준대로 바꾸기] · 사용자 = 어느 부재에서 겹친 만큼을 뺄지 고른다(JACK 2026-10-20 «겹침 기능처럼 사용자 모드도») —
            // 남을 쪽에서 빼기 = 지금 결합 그대로를 «사람이 정함»으로 기억(이미 사람이 정한 줄에는 두지 않는다 — 눌러도 아무 일 없는 단추 금지).
            if (row.Fine || row.Blocked) { if (ReversedPill(row) is { } done) _fixActions.Children.Add(done); return; }
            var (keep, cutter) = ShortNames(row.Winner, row.Loser);
            bool manual = row.Optional && row.Note.StartsWith(HumanNote, StringComparison.Ordinal);
            if (!s_userMode)
                _fixActions.Children.Add(ReversedButton("기준대로 바꾸기",
                    $"{cutter} 이 {keep} 을 자르던 결합을 바꿔, 겹친 자리를 {cutter} 에서 뺍니다(닫은 뒤 Ctrl+Z 한 번으로 되돌림).", "fix.reversed", cutKeep: false, primary: true));
            else
            {
                if (!manual)
                    _fixActions.Children.Add(ReversedButton(keep + " 에서 빼기",
                        $"{cutter} 은 그대로 두고 {keep} 에서 겹친 만큼을 뺍니다 — 지금 결합 그대로(기준과 반대). «사람이 정함»으로 기억해 다음 검사부터 걸지 않습니다.",
                        "fix.reversed.keep", cutKeep: true));
                _fixActions.Children.Add(ReversedButton(cutter + " 에서 빼기",
                    $"{keep} 은 그대로 두고 {cutter} 에서 겹친 만큼을 뺍니다(기준대로 — 자르는 순서를 바꿈).", "fix.reversed.cutter", cutKeep: false));
            }
            if (ReversedPill(row) is { } pill) { pill.Margin = new Thickness(4, 0, 0, 0); _fixActions.Children.Add(pill); }
            return;
        }
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
        button.MouseEnter += (_, _) => HoverEnter(button, () => SetPreview(cut, cutWinner));
        button.MouseLeave += (_, _) => HoverLeave(button, () => SetPreview(-1));
        button.Click += (_, _) => DoFix(cutWinner);
        return button;
    }

    // 고치기 단추에 마우스가 올라갔다 · 떠났다(겹침오류 · 작성오류 단추가 같이 쓴다). 떠날 때는 지금 잡고 있는 단추일 때만 끈다 — 고치기 줄을 다시 지어
    // 새 단추가 먼저 마우스를 받은 뒤 치운 옛 단추의 «떠남»이 늦게 와도 새 단추의 모습을 끄지 않게.
    // 고치기 줄을 다시 지은 직후(↑ ↓ 로 줄을 옮김 — 마우스는 그대로)의 «올라감»은 잠깐 뒤에 그린다: 글쇠를 누르고 있으면 줄마다 Revit 을 부르지 않게(계획 검토 L9).
    private void HoverEnter(Button button, Action view)
    {
        _hoverButton = button;
        _hoverView = view;
        _hoverDrawn = null;
        if (_fixBuilt.IsRunning && _fixBuilt.ElapsedMilliseconds < HoverSettleMs && SameSpot(Mouse.GetPosition(Root), _fixBuiltAt))
        {
            _hoverTimer ??= NewHoverTimer();
            _hoverTimer.Stop();
            _hoverTimer.Tag = button;
            _hoverTimer.Start();
            return;
        }
        ShowHover();
    }

    // 잡은 단추의 모습을 그린다(고치는 중이면 그리지 못한다 — 그때는 그려진 단추로 치지 않는다).
    private void ShowHover()
    {
        if (_hoverView == null) return;
        _hoverView();
        _hoverDrawn = _busy ? null : _hoverButton;
    }

    private Button? _hoverDrawn;                             // 그 모습이 지금 그림에 그려진 단추(엔터가 누를 수 있는 단추 — 설치 전 검토 낮음 4)

    // 떠날 때는 방향키로 본 모습으로 돌아간다(그림 = 엔터가 고칠 모습 — 계획 검토 중간 3) · 방향키로 고르지 않았으면 지금 모습.
    private void HoverLeave(Button button, Action off)
    {
        if (!ReferenceEquals(_hoverButton, button)) return;
        _hoverButton = null;
        _hoverView = null;
        _hoverDrawn = null;
        _hoverTimer?.Stop();
        off();
        if (_keyView is true && _board.Focused is { Fine: false, Blocked: false } row)
        {
            if (_item == Item.Reversed) SetReversedPreview(true);
            else SetPreview(row.LoserIndex);
        }
    }

    private const int HoverSettleMs = 150;
    private readonly Stopwatch _fixBuilt = new();           // 고치기 줄을 다시 지은 때부터
    private Point _fixBuiltAt;                               // 그때의 마우스 자리(그 뒤 움직이지 않았으면 줄을 옮겨 단추가 마우스 밑에 온 것)

    private static bool SameSpot(Point a, Point b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1;
    private System.Windows.Threading.DispatcherTimer? _hoverTimer;

    private System.Windows.Threading.DispatcherTimer NewHoverTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverSettleMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_busy && _window is { IsVisible: true } && ReferenceEquals(_hoverButton, timer.Tag)) ShowHover();
        };
        return timer;
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
        if (member >= 0 && _view3d != null && _m.Preview != null && _item == Item.Pairs && _pairs.Focused is { } row)
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
            if (shape.mesh != null && shape.at == member) Shape(member, shape.mesh);
        }
        _view3d?.Refresh();
    }

    // 작성오류 고치기 단추에 마우스를 올렸다(on) · 뗐다(JACK 2026-10-20 «기준대로 바꾸기와 사용자 모드의 두 단추에 마우스를 올리면 미리보기가 결합 후로»):
    // 두 부재를 부르는 쪽이 시험으로 자르는 순서를 바꿔 읽은 겉면으로 잠깐 바꿔 그린다(색은 그대로 — 겹침과 같다). 받은 모양은 고칠 때까지 다시 쓴다.
    // [남을 쪽에서 빼기](지금 결합 그대로)는 바꾼 뒤가 지금 모양이라 끄기만 한다(ReversedButton). 못 받으면 그림 위에 한 줄 알린다.
    private void SetReversedPreview(bool on)
    {
        if (_reversedPreview == on || (_busy && on)) return;
        if (on) EndSolo("고치기 단추에 마우스");
        EndShape();
        _reversedPreview = on;
        if (on && _view3d != null && _m.PreviewReversed != null && _item == Item.Reversed && _reversed.Focused is { } row)
        {
            if (!_reversedShapes.TryGetValue(row.Key, out var shapes))
            {
                // 부르는 동안(시험 트랜잭션이 열려 있다)은 고치기 · 닫기 · 글쇠 · 다른 미리보기를 막는다(설치 전 검토 미결 질문 — Revit 이 그사이 메시지를 돌릴 때).
                // 창을 누를 수 없게 하지는 않는다(그러면 마우스가 단추를 떠난 것으로 쳐 미리보기가 꺼진다). 돌아와서 그사이 떼었거나 다른 줄로 갔으면 그리지 않는다.
                var watch = Stopwatch.StartNew();
                _busy = true;
                try { shapes = _m.PreviewReversed(row.Key); }
                catch (Exception ex) { Note("작성오류 미리보기 오류\t" + ex); shapes = new Dictionary<int, MeshData>(); }
                finally { _busy = false; }
                _reversedShapes[row.Key] = shapes;
                Note($"작성오류 미리보기 {row.Key}\t{(shapes.Count == 0 ? "못 받음" : $"칸 {shapes.Count} · 세모 {shapes.Values.Sum(m => m.Triangles)}")}\t{watch.ElapsedMilliseconds} ms");
                if (!_reversedPreview || _item != Item.Reversed || _reversed.Focused?.Key != row.Key) { _view3d?.Refresh(); return; }
            }
            foreach (var (at, mesh) in shapes)
                if (at >= 0 && at < _members) Shape(at, mesh);
            if (shapes.Count == 0)
            {
                ShowMessage(PreviewFailText, true);
                _previewFailShown = true;
            }
        }
        else if (!on && _previewFailShown) HideMessage();  // 떼면 «못 그림» 알림도 내린다(설치 전 검토 낮음 2)
        if (!on) _previewFailShown = false;
        _view3d?.Refresh();
    }

    private const string PreviewFailText = "바꾼 뒤의 모양을 미리 그리지 못했습니다(까닭은 일지).";
    private bool _previewFailShown;                       // 그림 위 알림이 작성오류 미리보기의 «못 그림»이다

    // 그 칸을 미리보기 겉면으로 잠깐 바꾼다(EndShape 가 돌린다).
    private void Shape(int at, MeshData mesh)
    {
        if (!_shaped.Contains(at)) _shaped.Add(at);
        try { _view3d?.Replace(at, mesh); }
        catch (Exception ex)
        {
            _shaped.Remove(at);
            Note($"미리보기 모양을 그리지 못함 {at}\t" + ex);
        }
    }

    // 미리보기로 바꿔 둔 겉면을 그림이 실제로 가진 것(_drawn)으로 돌린다. 돌리지 못한 칸은 _shaped 에 남겨 다음에 다시 돌린다 —
    // 그동안 Apply 는 그 칸의 _drawn 을 그림에서 읽지 않는다(미리보기 겉면이 _drawn 에 굳지 않게 · R51 revit-verify ③).
    private void EndShape()
    {
        foreach (int at in _shaped.ToList())
        {
            try
            {
                _view3d?.Replace(at, _drawn[at]);
                _shaped.Remove(at);
            }
            catch (Exception ex) { Note($"미리보기 모양을 돌리지 못함 {at}\t" + ex); }
        }
    }

    // 덩어리를 그리는가: 덩어리가 있는 쌍 — 미리보기로 잘릴 부재의 모양을 바꿔 둔 동안 그 쌍의 덩어리는 숨긴다(겹친 부위가 남을 부재 안으로 들어간 모습).
    // 결합된 쌍(고친 쌍 · 결합한 뒤의 창의 결합됨)의 덩어리도 그리지 않는다 — 두 부재의 겉면은 결합 뒤 모양이라, 덩어리를 덮으면 고친 뒤에도
    // 겹친 덩어리가 그대로 있는 것처럼 보인다(JACK 2026-10-19 «결합됨으로 되면 수정된 상태로 보여야 되는데 계속 그대로»). 이미 풀린 자리(회색)는 그린다.
    private bool BlobShown(int key) =>
        _item == Item.Pairs && _pairs.LookOf(key) != BlobLook.Hidden && _pairs.Row(key) is not { Fine: true, Gone: false } && !(_shaped.Count > 0 && key == _pairs.Focus);

    /// <summary>고른 쌍을 고친다(고치기 단추와 같다) — cutWinner = 남는 쪽에서 겹친 만큼을 뺀다. 고칠 수 없으면(쌍을 안 골랐거나 끝난 쌍) 아무것도 하지 않는다.</summary>
    public void FixFocused(bool cutWinner) => DoFix(cutWinner);

    /// <summary>고른 결합 방향 줄을 고친다(그 단추와 같다) — cutKeep = 남을 쪽에서 뺀다(지금 결합 그대로를 «사람이 정함»으로). 고칠 수 없으면 아무것도 하지 않는다.</summary>
    public void FixFocusedReversed(bool cutKeep = false) => DoFixReversed(cutKeep);

    // 작성오류 고치기 단추(기본 · 사용자 모드 — 겹침의 FixButton 과 같은 꼴). 마우스를 올린 동안 고친 뒤의 모습: 기준대로(자르는 순서를 바꿈)면 바꾼 뒤 모양 ·
    // 남을 쪽에서 빼기(지금 결합 그대로)면 지금 모양 그대로(SetReversedPreview).
    private Button ReversedButton(string text, string tip, string id, bool cutKeep, bool primary = false)
    {
        var button = new Button
        {
            Style = _ui.StyleOf(primary ? "BtnPrimary" : "BtnSecondary"), Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 },
            MinHeight = 30, Padding = new Thickness(12, 0, 12, 1), Margin = new Thickness(0, 0, 6, 0), Focusable = false,
        };
        _ui.Mark(button, tip, id);
        button.MouseEnter += (_, _) => HoverEnter(button, () => SetReversedPreview(!cutKeep));
        button.MouseLeave += (_, _) => HoverLeave(button, () => SetReversedPreview(false));
        button.Click += (_, _) => DoFixReversed(cutKeep);
        return button;
    }

    private void DoFixReversed(bool cutKeep)
    {
        if (_busy || _m.FixReversed == null || _item != Item.Reversed || _board.Focused is not { } row || row.Fine || row.Blocked) return;
        Note($"작성오류 고치기 {row.Key}\t{(cutKeep ? "사용자: 남을 쪽에서 빼기(그대로 · 사람이 정함)" : s_userMode ? "사용자: 잘릴 쪽에서 빼기(기준대로)" : "기본")}");
        SetReversedPreview(false);                        // 미리보기로 바꿔 둔 겉면을 먼저 돌린다(고친 결과의 겉면이 그 위에 온다)
        Run(() => _m.FixReversed(row.Key, cutKeep), row.Key, _reversed);
    }

    // 창을 잠그고 부르는 쪽의 고치기를 부른 뒤 결과를 옮긴다(결합 방향 고치기 · 되돌리기가 같이 쓴다).
    // beforeApply = 옮기기 전에 창의 상태를 맞춘다(되돌린 겹침오류를 «손대지 않음»으로) · lockedBefore = Apply 의 것.
    private void Run(Func<Fixed> fix, int key, OverlapBoard board, Action<Fixed>? beforeApply = null, bool? lockedBefore = null)
    {
        var watch = Stopwatch.StartNew();
        var cursor = Mouse.OverrideCursor;
        Fixed result;
        _busy = true;
        Root.IsHitTestVisible = false;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            result = fix();
        }
        catch (Exception ex)
        {
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
        try
        {
            beforeApply?.Invoke(result);
            Apply(result, lockedBefore);
        }
        catch (Exception ex)
        {
            Note("고친 결과를 창에 옮기다 오류\t" + ex);
            ShowMessage("고친 결과를 그리다 오류가 났습니다" + (result.Changed ? "(모델은 바뀌었습니다)" : "") + ": " + ex.Message, true);
        }
        Note($"고친 뒤 {key}\t{board.Row(key)?.Status}\t바뀐 줄 {result.Rows.Count + result.ReversedRows.Count} · 겉면 {result.Meshes.Count}\t{watch.ElapsedMilliseconds} ms");
    }

    // 백스페이스(JACK 2026-10-21 «해당 목록만 초기화» · 고름 «고른 줄의 고친 것만 되돌리기»): 고른 줄을 고치기 전으로 — 부르는 쪽이 한다
    // (겹침오류 = 결합을 풂 · 작성오류 = 자르는 순서를 되돌림 · 사람이 정함 = 그 기록을 지움). 고친 것이 없는 줄은 한 줄 알림만.
    // 되돌린 겹침오류는 다시 «손대지 않음»이다(작성오류 단계가 다시 잠긴다 — 고친 뒤 [다음 단계]).
    private void Revert()
    {
        if (!_wide || _busy || _board.Focused is not { } row) return;
        bool pairs = _item == Item.Pairs;
        var revert = pairs ? _m.Revert : _m.RevertReversed;
        if (revert == null) return;
        bool done = pairs ? row.Status.Length > 0 && !row.Gone && !row.Blocked : row.Fine || row.Note.StartsWith(HumanNote, StringComparison.Ordinal);
        if (!done)
        {
            Note($"백스페이스 {row.Key}\t되돌릴 것 없음");
            ShowMessage("되돌릴 것 없음 — 아직 고치지 않은 줄");
            return;
        }
        Note($"백스페이스 {row.Key}\t되돌리기({(pairs ? "겹침오류" : "작성오류")})");
        SetPreview(-1);                                   // 미리보기로 바꿔 둔 겉면을 먼저 돌린다
        SetReversedPreview(false);
        Run(() => revert(row.Key), row.Key, _board, result =>
        {
            if (!pairs) return;
            _pairsChanged |= result.Changed;              // 모델이 바뀌었다 — [다음 단계]가 작성오류를 다시 찾게(줄의 결과와 상관없이 · 설치 전 검토)
            if (result.Rows.Any(r => r.Key == row.Key && r.Status.Length == 0)) _triedPairs.Remove(row.Key);
        }, ReversedLocked);
    }

    private void DoFix(bool cutWinner)
    {
        if (_busy || _m.Fix == null || _item != Item.Pairs || _board.Focused is not { } row || row.Fine || row.Blocked) return;
        Note($"고치기 {row.Key}\t{(s_userMode ? (cutWinner ? "사용자: 남는 쪽에서 빼기" : "사용자: 잘리는 쪽에서 빼기") : "기본")}");
        bool lockedBefore = ReversedLocked;
        _triedPairs.Add(row.Key);
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
            _pairsChanged = true;                         // 바뀌었는지 모른다 — [다음 단계]가 다시 찾게(계획 검토 L3)
            RefreshItems();                               // 불러 본 쌍이라 작성오류의 잠금이 풀렸을 수 있다
            RefreshChrome();                              // [다음 단계] 단추도(잠금이 풀리면 켠다 — R54c)
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
        _pairsChanged |= result.Changed;
        try { Apply(result, lockedBefore); }
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
    // lockedBefore = 고치기 전 작성오류의 잠금(겹침 고치기는 부르기 전에 그 쌍을 «손댐»에 넣으므로 그 앞에서 잰 것을 넘긴다).
    private void Apply(Fixed result, bool? lockedBefore = null)
    {
        // 새 줄 먼저(목록에 없는 번호만 · 같은 번호가 두 번 오면 앞의 것) — 같은 번호가 바뀐 줄에도 오면 그다음의 Replace 가 받는다(core-verify R50 중간 2).
        _cutShapes.Clear();                               // 모델이 바뀌었을 수 있다 — 받아 둔 미리보기 모양은 다시 받는다
        _reversedShapes.Clear();
        bool wasLocked = lockedBefore ?? ReversedLocked;
        var fresh = result.Added.Where(r => _pairs.Row(r.Key) == null).GroupBy(r => r.Key).Select(g => g.First()).ToList();
        int added = _pairs.AddRange(fresh);
        int refused = 0;
        foreach (var row in result.Rows)
            if (!_pairs.Replace(row)) { refused++; Note($"줄을 바꾸지 못함 {row.Key}"); }
        foreach (var row in result.ReversedRows)
            if (!_reversed.Replace(row)) { refused++; Note($"작성오류 줄을 바꾸지 못함 {row.Key}"); }
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
                _drawn[at] = _shaped.Contains(at) ? mesh : _view3d.MeshAt(at);
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
                if (_pairs.Row(key) is not { } row) continue;
                bool drawn = _blobAt.TryGetValue(key, out int at) && _drawn[at].Triangles > 0;
                if (row.HasBlob != drawn) _pairs.Replace(row with { HasBlob = drawn });
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
        // 작성오류의 잠금(JACK 2026-10-20 «겹침오류를 해결해야만 작성오류로»): 작성오류를 고치다 새 겹침오류가 나왔으면 겹침오류로 돌아간다.
        bool lockedNow = ReversedLocked, backToPairs = _item == Item.Reversed && lockedNow;
        if (backToPairs)
        {
            _item = Item.Pairs;
            _board = _pairs;
            if (fresh.Count > 0) _pairs.SetFocus(fresh[0].Key);   // 새 겹침오류를 바로 보이게(설치 전 검토 낮음 4)
            Note($"항목 겹침오류 — 새 겹침오류 {PairsUntried}쌍(작성오류 잠김) → {_pairs.Focus}");
        }
        RefreshChrome();
        RefreshList();
        RefreshFocus();
        if (added > 0 || backToPairs) BringRowIntoView(); // 보던 줄 위에 새 줄이 끼면 밀려 내려간다(core-verify R50 낮음 7)
        if (backToPairs) Frame();
        var problems = new List<string>();
        if (result.Problem.Length > 0) problems.Add(result.Problem);
        if (refused > 0) problems.Add($"목록의 {refused}줄에 고친 결과를 옮기지 못했습니다 — 창을 닫고 다시 실행해 확인해 주세요.");
        if (drawProblem.Length > 0) problems.Add(drawProblem);
        // 다시 찾기로 달라진 것(JACK 2026-10-19 «새로 생긴 겹침 n쌍 · 없어진 겹침 m쌍» — 0 인 쪽은 뺀다).
        var news = new List<string>();
        if (result.Said.Length > 0) news.Add(result.Said);
        if (added > 0) news.Add($"새로 생긴 겹침 {added}쌍");
        if (result.Gone > 0) news.Add($"없어진 겹침 {result.Gone}쌍");
        if (backToPairs) news.Add("새 겹침오류가 생겨 ① 로 돌아왔습니다 — 고친 뒤 [다음 단계]");
        else if (wasLocked && !lockedNow && _item == Item.Pairs) news.Add("겹침오류를 다 고쳤습니다 — [다음 단계]로 작성오류");
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
        StackPanel Item((byte r, byte g, byte b) color, string text, bool outline = false)
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
            return item;
        }
        _legendBlob = Item(BlobColor, "겹친 부위");
        _legendWinner = (TextBlock)Item(WinnerColor, "남는 쪽").Children[1];
        _legendLoser = (TextBlock)Item(LoserColor, "잘리는 쪽").Children[1];
        Item(_view3d!.GhostEdge, "나머지", outline: true);
        var legend = new Border { Padding = new Thickness(2, 7, 2, 0), Child = line };
        _ui.Mark(legend,
            "겹친 부위는 다른 부재에 가려져 있어도 보입니다. 두 부재는 쌍을 고르면 칠합니다 · 목록의 부재 이름을 누르면 그 부재만 칠합니다.\n" +
            "고치면(결합됨) 겹친 부위가 남는 쪽 안으로 들어간 모양이 됩니다 · 고치기 단추에 마우스를 올리면(→ 글쇠, ← = 지금) 그 모양이 미리 보입니다 · 엔터 = 본 모양대로 고치기 · 백스페이스 = 고른 줄 되돌리기 · 회색 = 앞선 결합으로 이미 풀린 자리.\n" +
            "돌리기 = 오른쪽 끌기 · 옮기기 = 가운데 끌기 · 확대 = 굴림" +
            (_pairs.Joined ? "\n결합한 뒤: 겹친 부위는 결합하기 전에 잰 모양입니다(다시 찾아 나온 쌍은 그때 잰 모양)." : ""), "legend");
        return legend;
    }

    // 그리는 방식: 덩어리는 맨 위(외곽선만으로 그릴 때는 둘레 부재처럼), 부재는 칠할 때만 옅은 면, 아니면 외곽선만.
    // 부재 이름을 눌렀으면(_solo) 그 부재만 옅은 면이고 나머지는 덩어리까지 외곽선만(고치기 단추에 마우스를 올리면 풀린다 — SetPreview).
    private MeshStyle StyleOf(int index)
    {
        bool solo = _solo >= 0 && _preview < 0;
        if (index >= _members) return solo || _pairs.LookOf(_blobKey[index - _members]) == BlobLook.Faint ? MeshStyle.Ghost : MeshStyle.Top;
        if (solo) return index == _solo ? MeshStyle.Tint : MeshStyle.Ghost;
        return _board.RoleOf(index) == OverlapRole.None ? MeshStyle.Ghost : MeshStyle.Tint;
    }

    // 그 겉면의 색: 덩어리 = 빨강(이미 풀린 자리 = 회색 · 고친 자리는 그리지 않는다 — BlobShown), 부재 = 남는 쪽 주황 / 잘리는 쪽 초록.
    // 고치기 단추에 마우스를 올린 동안에도 두 부재의 색은 그대로다(JACK 2026-10-19 «색은 그대로»). 잘릴 부재의 모양을 받지 못해 그 쌍의 덩어리가
    // 보이면 남을 부재의 색으로 칠한다(겹친 부위가 그 부재의 것이 된다).
    // (외곽선만인 것의 색은 쓰이지 않는다.)
    private (byte r, byte g, byte b) ColorOf(int index)
    {
        if (index >= _members)
        {
            int key = _blobKey[index - _members];
            if (_preview >= 0 && key == _pairs.Focus) return _pairs.RoleOf(_preview) == OverlapRole.Winner ? LoserColor : WinnerColor;
            return _pairs.LookOf(key) switch { BlobLook.Done => WinnerColor, BlobLook.Gone => LooseColor, _ => BlobColor };
        }
        return _board.RoleOf(index) == OverlapRole.Winner ? WinnerColor : LoserColor;
    }

    // 마우스 밑의 것을 적은 글.
    private string Describe(int index)
    {
        if (index >= _members)
        {
            var row = _pairs.Row(_blobKey[index - _members]);
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
            var keys = Picker.PickAll(_drawn, i => i >= _members && BlobShown(_blobKey[i - _members]), origin, direction)
                .Select(hit => _blobKey[hit.index - _members]).ToList();
            if (_item == Item.Pairs) _pairs.ClickBlobs(keys);
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
        _reversedPreview = false;
        _solo = -1;
        _view3d?.Refresh();
        MarkRows();
        HideMessage();
        var row = _board.Focused;
        _focusHead.Inlines.Clear();
        if (row == null)
        {
            string all = _item == Item.Reversed ? $"작성오류 {ReversedCount}쌍 전체"
                       : _pairs.Joined ? $"결합한 뒤 {Pairs}쌍 전체" : $"겹침오류 {Pairs}쌍 전체";
            _focusHead.Inlines.Add(new Run(all) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
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
            bool reversed = _item == Item.Reversed;
            Side(reversed ? "남을 쪽" : "남는 쪽", WinnerColor, winner, winnerId);
            _focusHead.Inlines.Add(new Run("   →   ") { Foreground = _ui["Sub"] });
            Side(reversed ? "잘릴 쪽" : "잘리는 쪽", LoserColor, loser, loserId);
            int at = _board.Ordered.ToList().FindIndex(r => r.Key == row.Key) + 1;
            string volume = row.Unmeasured ? "바뀔 체적 안 잼" : $"{(reversed ? "바뀔" : "겹친")} 체적 {row.VolumeM3.ToString("0.000", CultureInfo.InvariantCulture)} m³";
            string sub = reversed
                ? $"{(row.Fine ? "바꿈" : "지금은 반대로 결합됨")} · {volume} · {row.WinnerPart} → {row.LoserPart} · {at} / {_board.Ordered.Count}"
                : $"{volume} · {row.WinnerPart} → {row.LoserPart} · {at} / {_board.Ordered.Count}";
            if (_m.Fix == null && row.Status.Length > 0) sub += " · " + row.Status;    // 고치기 줄이 있으면 결과는 그 줄의 딱지로 보인다
            if (row.Split.Length > 0) sub += " · " + row.Split;                         // 조각 n개 / 나누기 못 함: 까닭
            if (_view3d != null && !row.HasBlob && !reversed) sub += " · 겹친 부위 그림 없음";   // 결합 방향은 덩어리가 없다(이미 결합돼 겹치지 않음)
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
        _previewFailShown = false;
    }

    private void HideMessage()
    {
        _toast.Visibility = Visibility.Collapsed;
        _previewFailShown = false;
    }

    private void Note(string what)
    {
        if (Trail.Count < 400) Trail.Add($"{DateTime.Now:HH:mm:ss.fff}\t{what}");
    }

    // Esc: 끌던 것을 그만두고 → 초점이 있으면 전체 보기로 → 없으면 닫는다. (단추에 IsCancel 을 주지 않았다 — 주면 초점이 있어도 한 번에 닫힌다.)
    // 방향키: Arrow(목록을 굴리거나 단추 사이를 옮겨 다니지 않게 창이 먼저 받는다).
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_busy) { e.Handled = true; return; }      // 고치는 중에는 글쇠를 받지 않는다
        if (e.Handled) return;
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            e.Handled = true;
            Arrow(e.Key);
            return;
        }
        if (e.Key is Key.Enter && _wide)
        {
            e.Handled = true;                             // 목록 단추에 글쇠 초점이 있어도 그 단추를 다시 누르지 않게
            if (e.IsRepeat || !_enterArmed) return;       // 누르고 있으면 되풀이하지 않는다 — 고친 뒤 새 줄 · 새 단추를 보지도 않고 고치지 않게
            Enter();
            return;
        }
        if (e.Key is Key.Back && _wide)
        {
            e.Handled = true;
            if (!e.IsRepeat) Revert();                    // 누르고 있어도 한 줄만
            return;
        }
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_view3d is { Pressed: true }) { _view3d.CancelDrag(); return; }
        if (_board.Focus >= 0) { Note("Esc → 전체 보기"); ShowAll(); return; }
        Finish(Choice.Close, -1);
    }

    // 방향키(JACK 2026-10-20 «위아래로 목록이 움직이고 · 좌우는 그 오류 줄의 객체의 전후 · 목록이 안 눌려 있으면 아무 행동 안 되게 — 키보드로만 조작»):
    // ↑ ↓ = 앞 · 다음 줄([이전] · [다음]과 같다) · → = 고친 뒤 모습(기본 모드의 고치기 단추에 마우스를 올린 것과 같다) · ← = 지금 모습.
    // 고른 줄이 없으면 아무것도 하지 않는다. 끝난 줄 · 이 창에서 고칠 수 없는 줄은 → 도 지금 모습이다(고친 뒤가 따로 없다).
    private void Arrow(Key key)
    {
        if (!_wide || _board.Focused is not { } row) return;
        if (key is Key.Up or Key.Down)
        {
            Step(key == Key.Down ? +1 : -1);
            return;
        }
        // 마우스가 고치기 단추 위면 그 단추가 지시하는 모습 그대로(JACK 2026-10-21 «무조건 버튼에서 지시하는 뷰»).
        if (_hoverView != null)
        {
            Note($"글쇠 {(key == Key.Right ? "→" : "←")} {row.Key}\t단추 위 마우스 — 단추의 모습 그대로");
            _hoverTimer?.Stop();
            ShowHover();
            return;
        }
        bool after = key == Key.Right && !row.Fine && !row.Blocked;
        Note($"글쇠 {(key == Key.Right ? "→" : "←")} {row.Key}\t{(after ? "고친 뒤" : "지금")}");
        _keyView = after;
        if (_item == Item.Reversed) SetReversedPreview(after);
        else SetPreview(after ? row.LoserIndex : -1);
    }

    // 엔터(JACK 2026-10-21 «좌우 방향키로 바뀌는 뷰를 확인한 상태에서 엔터를 치면 그 뷰대로 수정 — 단축키처럼»): 지금 그림이 보여 주는 모습의 고치기 단추를 누른다.
    //  · 마우스가 고치기 단추 위면 그 단추(그 단추의 모습이 보이고 있다).
    //  · → 로 본 고친 뒤 = 그 모습을 그리는 단추: 겹침오류 [이 쌍 고치기] · [잘리는 쪽에서 빼기] / 작성오류 [기준대로 바꾸기] · [잘릴 쪽에서 빼기].
    //  · ← 로 본 지금 = 지금 모습을 남기는 단추가 있을 때만: 작성오류 사용자 모드의 [남을 쪽에서 빼기](지금 결합 그대로 · 사람이 정함). 겹침오류에는 없다.
    //  · 방향키로 아직 고르지 않았으면 아무것도 하지 않는다(실수로 눌러 고치지 않게) · 끝난 줄 · 고칠 수 없는 줄도.
    // 아무것도 하지 않을 때는 그림 위에 한 줄(JACK 2026-10-21 «엔터를 눌러도 작동이 안 돼» — 마지막 글쇠가 ← 였다: 일지).
    private void Enter()
    {
        if (!_wide || _board.Focused is not { } row) return;
        if (row.Fine || row.Blocked)
        {
            Note($"엔터 {row.Key}\t{(row.Fine ? "끝난 줄" : "고칠 수 없는 줄")}이라 그대로");
            ShowMessage(row.Fine ? "이미 고친 줄 — 되돌리기 = 백스페이스" : "이 창에서 고칠 수 없는 줄");
            return;
        }
        if (_hoverButton is { IsEnabled: true } hovered)
        {
            if (!ReferenceEquals(_hoverDrawn, hovered))
            {
                // 그 단추의 모습이 아직 그림에 없다(줄을 옮긴 직후 · 고치는 중에 올라감) — 이번 엔터는 그리기만(본 적 없는 모습으로 고치지 않게).
                Note($"엔터 {row.Key}\t마우스 밑 단추 — 모습을 먼저 그림");
                _hoverTimer?.Stop();
                ShowHover();
                return;
            }
            Note($"엔터 {row.Key}\t마우스 밑 단추");
            _enterArmed = false;
            hovered.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, hovered));
            return;
        }
        const string LookFirst = "→ 로 고친 뒤 모습을 보고 엔터";
        if (_keyView is not bool after) { Note($"엔터 {row.Key}\t방향키로 본 모습이 없어 그대로"); ShowMessage(LookFirst); return; }
        if (_item == Item.Reversed)
        {
            if (after) { Note($"엔터 {row.Key}\t→ 본 모습대로(기준대로)"); _enterArmed = false; DoFixReversed(false); }
            else if (s_userMode && !(row.Optional && row.Note.StartsWith(HumanNote, StringComparison.Ordinal))) { Note($"엔터 {row.Key}\t← 본 모습대로(지금 그대로 · 사람이 정함)"); _enterArmed = false; DoFixReversed(true); }
            else { Note($"엔터 {row.Key}\t← 지금 모습을 남기는 단추가 없어 그대로"); ShowMessage("← 는 지금 모습 · " + LookFirst); }
            return;
        }
        if (after) { Note($"엔터 {row.Key}\t→ 본 모습대로"); _enterArmed = false; DoFix(false); }
        else { Note($"엔터 {row.Key}\t← 지금 모습은 고칠 것이 없어 그대로"); ShowMessage("← 는 지금 모습 · " + LookFirst); }
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
            button.Click += (_, _) => { if (!StepJustChanged) Finish(choice, -1); };
            DockPanel.SetDock(button, Dock.Right);
            bar.Children.Add(button);
        }

        // DockPanel 은 오른쪽에 붙이는 순서대로 바깥에서 안으로 놓는다 — 가장 오른쪽 단추를 먼저 넣는다.
        if (_wide && _m.Mode is Mode.Found or Mode.Joined)
        {
            StepButtons(bar, Right);
            return bar;
        }
        switch (_m.Mode)
        {
            case Mode.Found:
                // 아직 끝나지 않은 쌍만 건다(창에서 고친 쌍은 빼고) + 기준과 반대로 결합된 쌍의 순서도 바꾸고 · 결합돼 두 덩이인 벽 · 기둥도 나눈다(R54 — 단추 하나 ·
                // revit-verify 계획 H3). 할 것이 없으면 단추를 두지 않는다. 걸 쌍이 없고 나누기만 남았으면 이름표가 그것을 말한다.
                if (Left + ReversedLeft + _m.SplitToDo > 0)
                    Right(_ui.Button(JoinLabel(), "BtnPrimary", JoinTip(), "join"), Choice.Join);
                if (Pairs > 0)      // 결합 방향만 걸린 창에는 두지 않는다(누를 겹친 부재가 없다 — R54)
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
                // 겹친 쌍 · 결합 방향은 없지만 결합돼 두 덩이인 벽 · 기둥이 있다: 그것만 한다(결합 방향이 있으면 넓은 창이다 — R54).
                if (_m.Mode == Mode.Clean && _m.SplitToDo > 0)
                    Right(_ui.Button(JoinLabel(), "BtnSecondary", JoinTip(), "join"), Choice.Join);
                break;
        }
        return bar;
    }

    // 넓은 창의 아래 단추(R54c — 단계마다):
    //  ① [닫기] · [겹친 부재 전부 선택](결합하기 전) · [확인할 부재 선택하고 닫기](결합한 뒤 확인할 것) · [모두 기준대로 (겹침오류 n쌍 · 나누기 m개)] · [다음 단계: 작성오류 →]
    //     (겹침오류에 손대지 않은 쌍이 남으면 꺼지고 풍선 글 — 잠금은 R54b 그대로).
    //  ② [닫기] · [확인할 부재 선택하고 닫기] · [← 이전 단계: 겹침오류](창 안에서) · [모두 기준대로 (작성오류 n쌍)].
    // [모두 기준대로]는 그 단계 것만 한다(JACK 고름 «겹침오류만 → 작성오류 단계로» — 부르는 쪽이 Shown 으로 가른다). 결합한 뒤 ① 에서는 새로 나온 쌍이 있을 때만 둔다.
    private void StepButtons(DockPanel bar, Action<Button, Choice, bool, bool> right)
    {
        bool pairsStep = _item == Item.Pairs;
        if (pairsStep)
        {
            bool locked = ReversedLocked;
            bool joinable = (_pairs.Joined ? Pending > 0 : Left > 0) || _m.SplitToDo > 0;
            var next = _ui.Button("다음 단계: 작성오류  →", joinable || locked ? "BtnSecondary" : "BtnPrimary", "", "step.next");
            _ui.Tip(next, locked ? $"겹침오류를 먼저 고쳐 주세요 — 아직 고치지 않은 쌍 {PairsUntried}개"
                                 : _pairsChanged ? "고친 모델로 작성오류를 다시 찾아 ② 로 갑니다." : "② 작성오류로 갑니다.");
            next.IsEnabled = !locked;
            next.Margin = new Thickness(8, 0, 0, 0);
            next.Click += (_, _) => GoNext();
            DockPanel.SetDock(next, Dock.Right);
            bar.Children.Add(next);
            if (joinable) right(_ui.Button(JoinLabel(), "BtnPrimary", JoinTip(), "join"), Choice.Join, false, false);
        }
        else
        {
            // 맨 오른쪽 = [이전 단계](① 의 [다음 단계] 자리 — 두 번 눌러도 ② 의 [모두 기준대로]가 눌리지 않게 · 계획 검토 높음 1).
            if (Pairs > 0)
            {
                var back = _ui.Button("←  이전 단계: 겹침오류", "BtnSecondary", "① 겹침오류 목록으로 돌아갑니다(고친 결과는 그대로).", "step.back");
                back.Margin = new Thickness(8, 0, 0, 0);
                back.Click += (_, _) => { if (!_busy && !StepJustChanged) ShowItem(Item.Pairs); };
                DockPanel.SetDock(back, Dock.Right);
                bar.Children.Add(back);
            }
            if (ReversedLeft > 0) right(_ui.Button(JoinLabel(), "BtnPrimary", JoinTip(), "join"), Choice.Join, false, false);
        }
        if (_m.Mode == Mode.Joined && Problems > 0)
            right(_ui.Button($"확인할 부재 선택하고 닫기 ({Problems}쌍)", "BtnSecondary", "결합하지 못했거나 겹침이 남은 부재 · 못 바꾼 작성오류의 부재들이 선택된 채로 창이 닫힙니다.", "selectProblems"),
                  Choice.SelectProblems, false, false);
        else if (pairsStep && !_pairs.Joined && Pairs > 0)
            right(_ui.Button("겹친 부재 전부 선택", "BtnSecondary",
                "모델은 바꾸지 않습니다. 겹친 부재들이 Revit 에서 선택된 채로 창이 닫힙니다(창에서 고친 쌍은 빼고).", "selectAll"), Choice.SelectAll, false, false);
        right(_ui.Button("닫기", "BtnGhost", _m.Mode == Mode.Joined ? $"결합한 부재의 판정(DH_판정)은 '{_m.StaleVerdict}'로 바꿔 두었습니다 — [수량산출]의 [완료]가 식을 다시 맞춥니다." : "", "close"),
              Choice.Close, false, true);
    }

    // [모두 기준대로] · 작은 창 단추의 이름표 — 넓은 창과 작은 창이 같은 함수로 고른다(R38 core M-2 · L-3).
    // 단계마다 그 단계 것만(R54c): ① 겹침오류 + 두 덩이 나누기 · ② 작성오류. 나누기는 새 부재를 만드는 큰 변경이라 그 수를 이름표에 보인다.
    private string JoinLabel()
    {
        bool wide = _wide && _m.Mode is Mode.Found or Mode.Joined;
        // 결합한 뒤 ① 의 두 번째 일괄은 새로 나온 쌍만 건다(못 함 · 겹침 남음은 다시 걸지 않는다 — 부르는 쪽과 같게).
        int pairs = wide && _item == Item.Pairs ? (_pairs.Joined ? Pending : Left) : 0, order = wide && _item == Item.Reversed ? ReversedLeft : 0;
        int split = !wide || _item == Item.Pairs ? _m.SplitToDo : 0;
        var what = new List<string>();
        if (pairs > 0) what.Add($"겹침오류 {pairs}쌍");
        if (order > 0) what.Add($"작성오류 {order}쌍");
        if (what.Count == 0) return $"두 덩이 벽·기둥 나누기 ({split}개)";
        if (split > 0) what.Add($"나누기 {split}개");
        return $"모두 기준대로 ({string.Join(" · ", what)})";
    }

    private string JoinTip()
    {
        var lines = new List<string>();
        bool wide = _wide && _m.Mode is Mode.Found or Mode.Joined;
        if (wide && _item == Item.Pairs && Left > 0)
            lines.Add("겹침오류 전부에 Revit '형상 결합'을 걸어 순위가 낮은 쪽에서 겹친 만큼을 뺍니다 — 끝나면 고친 모델로 작성오류를 다시 찾아 ② 로 갑니다.\n기준: " + _m.PriorityText);
        if (wide && _item == Item.Reversed && ReversedLeft > 0) lines.Add($"작성오류 {ReversedLeft}쌍(기준과 반대로 결합됨): 순위가 높은 쪽이 남도록 자르는 순서를 바꿉니다.");
        if (_m.SplitToDo > 0 && (!wide || _item == Item.Pairs)) lines.Add($"결합돼 두 덩이인 벽·기둥 {_m.SplitToDo}개: 덩이마다 나눕니다(위치·부위는 그대로).");
        lines.Add("모델이 바뀝니다(닫은 뒤 Ctrl+Z 한 번으로 되돌림).");
        return string.Join("\n", lines);
    }

    // ── 기준표(R54 — 겹친 자리 주인표) ─────────────────────────────────────────────

    /// <summary>
    /// [기준표]의 내용: 순위 · 이름 · 든 부위(처음 값) · 까닭 + 이 모델에서 처음 값과 다르게 정한 줄. 바꾸는 곳은 ① 이라고 한 줄(설명은 최소 — JACK 1014).
    /// 시험(tools/uipreview)이 그림으로 뽑는다.
    /// </summary>
    public FrameworkElement RankTableContent()
    {
        var grid = new Grid();
        foreach (double width in new[] { 34.0, 96, 250, 330 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        void Cell(int row, int column, string text, bool head = false, bool strong = false, bool sub = false)
        {
            var block = new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 5, 10, 5), VerticalAlignment = VerticalAlignment.Top,
                FontSize = head ? 12 : 12.5, Foreground = head || sub ? _ui["Sub"] : _ui["Text"], FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }
        void Rule(int row)
        {
            var line = new Border { BorderBrush = _ui["Line"], BorderThickness = new Thickness(0, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 4);
            grid.Children.Add(line);
        }
        grid.RowDefinitions.Add(new RowDefinition());
        Cell(0, 0, "순위", head: true);
        Cell(0, 1, "이름", head: true);
        Cell(0, 2, "든 부위(처음 값)", head: true);
        Cell(0, 3, "까닭", head: true);
        Rule(0);
        foreach (var r in DeductRank.Rows)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition());
            Cell(row, 0, r.Rank.ToString(CultureInfo.InvariantCulture), strong: true);
            Cell(row, 1, r.Name, strong: true);
            Cell(row, 2, r.Parts);
            Cell(row, 3, r.Why, sub: true);
            Rule(row);
        }
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "겹친 자리 기준표", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(new TextBlock { Text = "숫자가 작은 쪽이 겹친 자리를 가지고, 큰 쪽에서 뺍니다 · 같은 순위면 «나중에 침»이 짐 → 기초 › 바닥 › 기둥 › 벽 › 보 → 먼저 만든 부재", Foreground = _ui["Sub"], FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        stack.Children.Add(new Border { Background = _ui["Card"], BorderBrush = _ui["Line"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 4, 4), Child = grid });
        if (_m.RankChanged.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "이 모델에서 바꾼 줄", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var (part, rank, first) in _m.RankChanged)
                stack.Children.Add(new TextBlock { Text = $"{part}   {DeductRank.Label(first)} → {DeductRank.Label(rank)}", Foreground = _ui["AccentText"], Margin = new Thickness(0, 1, 0, 1) });
        }
        stack.Children.Add(new TextBlock { Text = "바꾸는 곳: [수량산출] ① 부위 목록 › 고치기 › «겹친 자리»", Foreground = _ui["Sub"], FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        var root = new Border { Background = Background, Padding = new Thickness(18, 16, 18, 16), Child = stack, UseLayoutRounding = true, SnapsToDevicePixels = true };
        root.Resources.MergedDictionaries.Add(_ui.Resources);
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _ui["Text"]);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        return root;
    }

    private void ShowRankTable()
    {
        try
        {
            var content = RankTableContent();
            var close = _ui.Button("닫기", "BtnPrimary", id: "rankTable.close");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 0, 18, 14);
            close.IsDefault = close.IsCancel = true;
            var dock = new DockPanel { Background = Background };
            DockPanel.SetDock(close, Dock.Bottom);
            dock.Children.Add(close);
            dock.Children.Add(content);
            var window = new Window
            {
                Title = "DH 수량산출 — 겹친 자리 기준표", Content = dock, Background = Background, SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = _window != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (_window != null) window.Owner = _window;
            close.Click += (_, _) => window.Close();
            window.ShowDialog();
        }
        catch (Exception ex) { ShowMessage("기준표를 열지 못했습니다: " + ex.Message, true); }
    }

    private void Finish(Choice choice, int key)
    {
        if (_busy) return;                                // Revit 을 부르는 중(고치기 · 미리보기)에는 닫지 않는다
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
            window.PreviewKeyUp += (_, e) => { if (e.Key == Key.Enter) _enterArmed = true; };   // 3D 가 없는 창은 글쇠 초점이 창에 있다(설치 전 검토 낮음 3)
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
        window.Closed += (_, _) => _hoverTimer?.Stop();   // 닫힌 뒤 옛 창의 미리보기를 부르지 않게(설치 전 검토 낮음 5)
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
