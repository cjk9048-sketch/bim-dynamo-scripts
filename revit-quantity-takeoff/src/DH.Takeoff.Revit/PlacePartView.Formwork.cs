using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Run
using System.Windows.Input;
using System.Windows.Markup; // XamlReader
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 수량산출 창의 ② 거푸집면 설정(JACK 2026-10-15 «다음 단계를 누르면 해당 창 그대로에서 3D 뷰 위치부분은 고정되고 나머지만 다시 배치 ·
/// 거푸집면 설정단계도 위치정하기 기능처럼 3D 뷰를 보면서» — 인터뷰: 대응표 + 3D).
/// 3D 그림은 ① 의 그 자리 그대로다(카메라도). 부재는 옅은 회색, 거푸집은 완성된 판으로 종류의 색을 칠한다(R48 — JACK 2026-10-16 «완성된 판으로»:
/// FormworkBoard 의 덩이 = 엑셀 줄 · Revit 판과 같은 한 장 — 면에서 4mm 띄운 겉면 · 색은 Revit 3D 뷰의 덧그림과 같다).
///  · 왼쪽 판 = 대응표(부위 줄 · 유형 × 면 → 종류): 줄을 누르면 그 줄의 면이 든 판이 맨 위에 제 색으로, 부재와 다른 거푸집은 옅게 보이고
///    (R48 — «고른 줄만 진하게, 나머지 흐리게»), 종류를 바꾸면 대응표만 다시 걸어(형상은 다시 재지 않는다) 3D 의 판 · 색과 합계가 바로 바뀐다.
///    대응표는 바꿀 때마다 파일에 적는다(SaveRules).
///  · 그림 위 = 거푸집 합계 · 종류별 넓이 · 확인할 자리 수 · 찍은 판의 띠([지우기] · [되살리기] — R48 · [옮기기] · [원래 줄로] — R55b), 그림 아래 = 색 풀이.
///  · 3D 에서 판을 찍으면 흰 막 + 분홍 테두리로 덮고 그 판을 정한 줄을 고른다. 종류 · 층 거름은 ① 과 같이 쓴다(① 에서 숨긴 부재는 숨긴 채다).
/// </summary>
public sealed partial class PlacePartView
{
    private FormworkBoard? _board;
    private FrameworkElement? _fwSide, _fwTop, _fwBottom;
    private readonly StackPanel _fwRows = new();
    // 표 맨 아래 «지운 판» 묶음(R56a — JACK 2026-10-22 «지운 객체는 목록 맨 아래 보여야 새로 지정을 하든 할 텐데» · 고름 «판마다 한 줄»).
    private readonly StackPanel _fwDropRows = new();
    private readonly Dictionary<string, Border> _fwDropLines = new(StringComparer.Ordinal);
    private string? _fwDropKey;                  // 지운 판 묶음을 마지막으로 지은 열쇠(지운 판이 바뀔 때만 다시 짓는다 · null = 새 판이라 늘 다시)
    private readonly TextBlock _fwTotal = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _fwKinds = new() { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
    // 경고 딱지(그림에 없는 틈 · 종류가 달라 못 이은 면): 합계 줄 끝에 이으면 좁은 창에서 «…»로 잘려 안 보인다 — 따로 줄바꿈되게 둔다(revit R43 R4).
    private readonly WrapPanel _fwWarnings = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<(string type, string group), (Border row, ComboBox combo, TextBlock area, Border swatch, TextBlock tag, Button reset)> _fwRowViews = new();
    // 아래 줄(R64 — JACK 2026-10-26 «여러 종류는 풀어서 종류별로» · 고름 «종류별 아래 줄로 풀기»): 줄 밑 «└» 줄 · 아래 줄을 보이는 줄(보드를 지을 때 정함 — 보드 동안 그대로 · critic R64 M3).
    private readonly Dictionary<(string type, string group, string sub), (Border row, ComboBox combo, TextBlock area, Border swatch, TextBlock tag, Button reset)> _fwSubViews = new();
    private readonly HashSet<(string type, string group)> _fwSubRows = new();
    private string? _fwPickedSub;      // 고른 줄의 아래 줄(null = 줄 전부)
    private readonly StackPanel _fwKindBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };   // 찍은 판의 줄 종류 단추(JACK 2026-10-21 «3D 에서 찍어서»)
    private readonly Dictionary<string, Button> _fwKindButtons = new();
    private Button? _fwKindAuto;
    private readonly TextBlock _fwKindScope = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private (string type, string group)? _fwPicked;
    private string _fwTotalText = "";
    // 찍은 판(R48): 대표 조각으로 기억한다 — 대응표를 고치거나 판을 빼면 덩이를 다시 지어 차례가 바뀌지만 조각은 그대로다.
    private FacePatch? _fwPlate;
    private IReadOnlyList<MeshData> _fwMemberMeshes = Array.Empty<MeshData>();   // ② 의 부재 겉면(덮인 모서리 선을 뺀 것 — R48b)
    private int _fwPickedIndex = -1;                                   // 찍은 판의 그림 차례(없으면 −1)
    private MeshStyle[] _fwStyle = Array.Empty<MeshStyle>();           // 차례마다 그리는 방식(고른 것이 바뀔 때 한 번 정해 둔다)
    private readonly TextBlock _fwPlateText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
    private Button? _fwPlateButton;
    private Border? _fwPlateBar;

    // 고른 줄의 덧그림(R57 P1b · R58d · R59): 줄(② 대응표 줄 · ③ 산출서의 거푸집 줄)에 든 판 가운데 다른 줄과 이은 판(벽 + 슬래브 옆 · 높이 구간에 걸친 판)은
    // 윤곽선으로 두고 그 줄 몫만 덧그린다. ② 의 [산출서] 탭은 ③ 산출서 확인으로 옮겼다(R59 — JACK 2026-10-23 고름 «없애고 ③ 으로 모으기»).
    private readonly List<(MeshData mesh, int piece, FacePatch lead)> _fwOverlay = new();   // 두 줄에 걸친 판의 고른 줄 몫(덩이 차례 뒤에 그린다 · 색 = 그 몫의 첫 조각)
    private readonly HashSet<int> _fwLineWhole = new();                 // 통째로 고른 줄인 덩이(대응표 줄 · ③ 의 거푸집 줄)
    private object? _fwOverlayFor;                                      // 덧그림을 지은 고른 것(산출서 줄의 조각 · 대응표 줄의 열쇠) — 바뀌면 다시 짓는다

    /// <summary>[이전 단계](① 로 돌아간 뒤에 알린다) · [완료](③ 에서) · ③ 에서 [이전 단계](② 로 돌아간 뒤).</summary>
    public event Action? Back, Finish, ReviewBack;

    /// <summary>대응표를 모델에 적는다("" = 됨, 아니면 까닭) — 종류를 바꿀 때마다 부른다(창을 그냥 닫아도 고친 것이 남게).</summary>
    public Func<string>? SaveRules { get; set; }

    /// <summary>② 의 상태(아직 ② 에 간 적이 없으면 null).</summary>
    public FormworkBoard? Board => _board;

    private int MemberCount => _memberMeshes.Count;

    /// <summary>② 거푸집면 설정으로 바꿔 놓는다. 3D 의 보는 자리는 그대로다. 같은 board 로 다시 오면 고른 줄도 그대로다.</summary>
    public void ShowFormwork(FormworkBoard board)
    {
        if (Step == 2) ClearPicks();                   // ③ 에서 돌아옴 — ③ 에서 고른 것을 비운다(계획 검토 H1)
        bool fresh = !ReferenceEquals(board, _board);
        _board = board;
        _fwSide ??= BuildFormworkSide();
        _fwTop ??= BuildFormworkTop();
        if (fresh || _fwBottom == null) _fwBottom = BuildFormworkBottom();   // 색 풀이는 판마다(① 에서 고쳐 다시 지은 판은 높이 구간이 켜지거나 꺼질 수 있다 — revit-verify R53a L1)
        if (fresh)
        {
            _fwPicked = null;
            _fwPickedSub = null;
            _fwPlate = null;
            BuildFormworkRows();
            // ② 의 부재 겉면 = 덩이가 덮은 면의 모서리 선을 뺀 것(R48b — 판 뒤의 모서리가 비쳐 보이지 않게). 덮는 조각의 모임은 대응표를 바꿔도 같아 한 번만 짓는다.
            var watch = Stopwatch.StartNew();
            _fwMemberMeshes = _memberMeshes.Select((m, i) => board.WithoutCoveredEdges(m, _members[i].Id)).ToList();
            Log?.Invoke($"② 부재 모서리\t덮인 선분 뺌: 선분 {_memberMeshes.Sum(m => m.Segments)} → {_fwMemberMeshes.Sum(m => m.Segments)} · {watch.ElapsedMilliseconds} ms");
        }
        Step = 1;
        _side.Child = _fwSide;
        _top.Child = _fwTop;
        _bottom.Child = _fwBottom;
        // 그리는 법을 먼저 갈아 끼운다 — SetMeshes 가 새 차례로 바로 그린다.
        _view3d.Shown = FormworkShown;
        _view3d.ColorOf = FormworkColor;
        _view3d.Picked = FormworkPicked;
        _view3d.StyleOf = FormworkStyle;
        // 줄 · 판을 고른 동안 윤곽선만 그린 부재는 찍히지 않는다 — 그 뒤의 판을 찍어 바로 옮겨 고를 수 있게(부재를 찍어도 ② 에서는 할 일이 없다).
        // 윤곽선만 그린 판은 찍힌다(GhostPickable — JACK 2026-10-21 «고른 줄만 색 · 나머지는 투명에 윤곽선만»이어도 판을 눌러 옮겨 고르게).
        _view3d.Pickable = i => i < OverlayStart && (i >= MemberCount || FormworkStyle(i) == MeshStyle.Solid);   // 덧그린 줄 몫은 찍지 않는다(그 밑의 판을 찍는다)
        _view3d.GhostPickable = true;
        SetFormworkMeshes();
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ② 의 그릴 겉면 = 부재 + 덩이(판). 대응표를 고치거나 판을 빼면 덩이를 다시 지으므로 그때마다 갈아 끼운다(보는 자리는 그대로 —
    // 바뀌지 않은 판의 겉면은 그림이 다시 쓴다). 고른 것의 모습을 먼저 새 차례로 정한다(SetMeshes 가 바로 그린다).
    private void SetFormworkMeshes() => SetFormworkMeshes(selectionOnly: false);

    // selectionOnly = 고른 줄만 바뀌었다(덩이는 그대로) — 덧그릴 것이 전에도 지금도 없으면 겉면을 갈아 끼우지 않는다(마우스 밑 표시 · 그림을 그대로 두게).
    private void SetFormworkMeshes(bool selectionOnly)
    {
        if (_board == null) return;
        bool hadOverlay = _fwOverlay.Count > 0;
        // 고른 줄(② 대응표 줄 — JACK 2026-10-23 «상부슬래브를 찍었는데 벽체도 같이» · 고름 «그 줄 몫만 진하게» / ③ 산출서의 거푸집 줄 — R59)의 덩이 · 덧그릴 몫:
        // 통째로 그 줄인 덩이는 진하게 · 다른 줄과 이은 판(벽 + 슬래브 옆)은 윤곽선으로 두고 그 줄 몫만 덧그린다.
        _fwOverlay.Clear();
        _fwLineWhole.Clear();
        _fwOverlayFor = OverlayKey();
        var parts = Step == 2 ? (_rvFormLine != null ? _board.PiecesOfLine(_rvFormLine) : new List<(int piece, bool whole, List<FacePatch> patches)>())
            : _fwPicked is { } row ? _board.PiecesOfRow(row.type, row.group, _fwPickedSub)
            : new List<(int piece, bool whole, List<FacePatch> patches)>();
        foreach (var (piece, whole, patches) in parts)
        {
            if (whole) _fwLineWhole.Add(piece);
            else _fwOverlay.Add((_board.PatchMesh(patches), piece, patches[0]));
        }
        if (selectionOnly && !hadOverlay && _fwOverlay.Count == 0) { UpdateFormworkLook(); return; }
        var meshes = new List<MeshData>(_memberMeshes.Count + _board.Pieces.Count + _fwOverlay.Count);
        meshes.AddRange(_fwMemberMeshes.Count == _memberMeshes.Count ? _fwMemberMeshes : _memberMeshes);
        meshes.AddRange(_board.Pieces.Select(p => p.Mesh));
        meshes.AddRange(_fwOverlay.Select(o => o.mesh));
        UpdateFormworkLook();
        _view3d.SetMeshes(meshes);
    }

    /// <summary>① 부위 · 중분류로 되돌려 놓는다(고른 부재 · 보는 자리는 그대로).</summary>
    public void ShowPlaces()
    {
        Step = 0;
        _side.Child = _placeSide;
        _top.Child = _pickedBand;
        _bottom.Child = _stateBand;
        _view3d.Shown = i => _editor.IsShown(_members[i]);
        _view3d.ColorOf = i => ColorOf(_members[i]);
        _view3d.Picked = i => _editor.Selection.Contains(_members[i].Id);
        _view3d.StyleOf = _ => MeshStyle.Solid;
        _view3d.Pickable = _ => true;
        _view3d.GhostPickable = false;
        _view3d.SetMeshes(_memberMeshes);
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ── 3D ───────────────────────────────────────────────────────────────────────

    private FormworkPiece PieceAt(int index) => _board!.Pieces[index - MemberCount];

    // 그림 차례: 부재 → 덩이 → 덧그린 줄 몫(R57 P1b). 덧그린 몫은 그 밑 덩이의 거름 · 색을 따른다.
    private int OverlayStart => MemberCount + (_board?.Pieces.Count ?? 0);
    private int OverlayPiece(int index) => index >= OverlayStart && index - OverlayStart < _fwOverlay.Count ? MemberCount + _fwOverlay[index - OverlayStart].piece : -1;

    // 부재와 그 부재의 덩이는 같은 거름(종류 · 층 · 숨긴 부재)을 탄다. '남은 것만 보기'는 보지 않는다(다 넣은 부재에도 거푸집이 있다).
    // 부재를 넘어 이은 판은 든 부재 하나라도 보이면 보인다.
    private bool FormworkShown(int index)
    {
        if (index < MemberCount) return _editor.InView(_members[index]);
        if (index >= OverlayStart) return OverlayPiece(index) is int under && under >= 0 && FormworkShown(under);
        foreach (var m in PieceAt(index).Members)
            if (_editor.Find(m.Result.Element.Id) is not { } member || _editor.InView(member)) return true;
        return false;
    }

    // 부재 = 옅은 회색. 판 = 종류의 색(확인할 자리는 분홍) · 세지 않기로 한 덩이는 부재와 같은 회색 · 사람이 뺀 판은 옅은 회갈색(R48).
    private (byte r, byte g, byte b) FormworkColor(int index)
    {
        var grey = (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
        if (index < MemberCount) return _rvMembers.Contains(_members[index].Id) ? ConcreteColor : grey;   // 문제 딱지로 고른 부재(R63b — ② 도 ③ 처럼 청록)
        if (index >= OverlayStart)
        {
            // 덧그린 몫은 그 줄의 색(높이 구간이 다른 줄이면 밑 판의 대표와 색이 다르다 — P1b 검토 L1).
            if (index - OverlayStart >= _fwOverlay.Count) return grey;
            var part = _fwOverlay[index - OverlayStart].lead;
            return part.Counted || part.NeedsCheck ? FormworkPalette.Of(part) : grey;
        }
        var piece = PieceAt(index);
        if (piece.Dropped) return FormworkPalette.Dropped;
        var lead = piece.Lead;
        return lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : grey;
    }

    // 흰 막 + 분홍 테두리: 찍은 판(R48) · [완료]의 산출식 확인 창이 보여 주는 부재. 고른 줄은 막 대신 그 줄의 판을 맨 위에 진하게 그린다(FormworkStyle).
    private bool FormworkPicked(int index) =>
        index < MemberCount ? _fwFocus is long focus && _members[index].Id == focus : index < OverlayStart && index == _fwPickedIndex;

    // 그리는 방식(R48 — JACK 2026-10-16 «고른 줄만 진하게, 나머지 흐리게» · 2026-10-21 «목록을 누르면 그 자리만 색 · 나머지는 색 없는 투명에 윤곽선만»):
    // 고른 것이 없으면 모두 보통(면 + 모서리). 줄이나 판을 고르면 그 줄의 판(줄에 든 조각이 하나라도 있는 판 — 이은 판은 통째로)과 찍은 판은 맨 위에 제 색 ·
    // 부재와 다른 거푸집은 면 없이 윤곽선만(Ghost).
    private MeshStyle FormworkStyle(int index) => index < _fwStyle.Length ? _fwStyle[index] : MeshStyle.Solid;

    // 고른 줄 · 찍은 판으로 그림 차례마다의 모습을 정해 둔다(그리기 · 찍기가 차례마다 묻는다 — 마우스가 움직일 때마다 줄을 다시 세지 않게).
    // 찍은 판은 대표 조각으로 다시 찾는다(덩이를 다시 지어 차례가 바뀌어도) — 없어졌으면 푼다.
    private void UpdateFormworkLook()
    {
        int count = OverlayStart + _fwOverlay.Count;
        _fwPickedIndex = _board != null && _fwPlate != null && _board.IndexOf(_fwPlate) is int at && at >= 0 ? MemberCount + at : -1;
        if (_fwPickedIndex < 0) _fwPlate = null;
        var styles = new MeshStyle[count];
        if (_rvMembers.Count > 0)
        {
            // ③ 콘크리트 줄 · 미산출 부재(R59) · 문제 딱지(R63b — ② 도): 그 부재만 진하게 · 나머지 부재는 윤곽선 · 판은 숨김(③ ReviewShown) · 윤곽선(②).
            for (int i = 0; i < MemberCount; i++) styles[i] = _rvMembers.Contains(_members[i].Id) ? MeshStyle.Top : MeshStyle.Ghost;
            for (int i = MemberCount; i < count; i++) styles[i] = MeshStyle.Ghost;
        }
        else if (_board != null && ((Step == 2 ? _rvFormLine != null : _fwPicked != null) || _fwPickedIndex >= 0))
        {
            // 고른 줄(② 대응표 · ③ 거푸집 줄): 통째로 그 줄인 덩이와 덧그린 몫(다른 줄과 이은 판은 윤곽선만 — 그 줄 몫만 진하게).
            var inRow = _fwLineWhole;
            for (int i = 0; i < count; i++)
                styles[i] = i >= OverlayStart ? MeshStyle.Top
                    : i >= MemberCount && (inRow.Contains(i - MemberCount) || i == _fwPickedIndex) ? MeshStyle.Top : MeshStyle.Ghost;
        }
        _fwStyle = styles;
    }

    private long? _fwFocus;     // [완료]의 산출식 확인 창이 보여 주는 부재(② 에서 — 흰 막으로 덮는다)
    private int _changedCycle;  // «모양이 바뀐 판» 딱지를 누를 때마다 다음 판(R63b)

    /// <summary>
    /// 그 부재를 고른 채 그림 가운데에 보인다(산출식 확인 창이 부재를 하나씩 띄울 때). 0 = 풀기.
    /// ② 에서는 대응표의 줄 대신 그 부재를 덮고, ① 에서는 그 부재를 고른다(넣는 일은 하지 않는다).
    /// </summary>
    public void FocusMember(long id)
    {
        int index = IndexOf(id);
        if (Step >= 1)
        {
            _fwFocus = id > 0 ? id : null;
            if (id > 0)
            {
                _fwPicked = null;
                _fwPickedSub = null;
                _fwPlate = null;
            }
        }
        else _editor.Select(id > 0 && index >= 0 ? new[] { id } : Array.Empty<long>());
        RefreshAll();
        if (index >= 0) _view3d.ZoomTo(index);
    }

    // 판을 찍으면 그 판을 흰 막으로 덮고(그림 위 띠에 엑셀 줄 한 줄 + [이 판 빼기] · [되살리기]) 그 판을 정한 줄을 고른다
    // (대표 조각의 줄 — FormworkBoard.RowOf · 목록이 그 줄까지 굴러간다 · 뺀 판은 줄이 없다). 부재의 거푸집이 없는 면 · 빈 곳은 그대로 둔다(Esc 로 푼다).
    private void ClickFormwork(int index)
    {
        if (_board == null || index < MemberCount || index >= OverlayStart) return;
        var piece = PieceAt(index);
        _fwPlate = piece.Lead;
        _fwMoveLead = null;                       // 다른 판을 찍으면 옮길 줄 고르기는 그만둔다(R55b)
        _rvMembers.Clear();                       // 문제 딱지로 고른 부재는 푼다(R63b)
        _fwPicked = _board.RowOf(piece);
        _fwPickedSub = _fwPicked is { } shown && _fwSubRows.Contains(shown) ? _board.SubOf(piece) : null;   // 아래 줄을 보이는 줄이면 그 아래 줄(R64)
        RefreshAll();
        if (_fwPicked is { } key)
        {
            if (_fwPickedSub is { } sub && _fwSubViews.TryGetValue((key.type, key.group, sub), out var subView)) subView.row.BringIntoView();
            else if (_fwRowViews.TryGetValue(key, out var view)) view.row.BringIntoView();
        }
        FollowPiece(piece);   // Revit 에서도 그 판의 부재(R61a)
    }

    // 표에서 줄을 골랐다(또는 Esc 로 풀었다): 찍은 판은 푼다. 줄을 고르면 그 줄의 판이 다 보이게 화면을 맞춘다(JACK 2026-10-21 «포커싱도»).
    private void PickRow((string type, string group)? key, string? sub = null)
    {
        // 옮길 줄을 고르는 중이면(R55b [옮기기]) 누른 줄로 옮긴다 — 줄을 고르지 않는다(아래 줄을 눌러도 그 줄로 — critic R64 M4).
        if (key is { } target && _fwMoveLead != null) { MoveTo(target); return; }
        _fwPicked = key;
        _fwPickedSub = key == null ? null : sub;
        _fwPlate = null;
        _rvMembers.Clear();                       // 문제 딱지로 고른 부재는 푼다(R63b — Esc 도 이 길)
        RefreshAll();
        if (key == null) CancelFollow();   // Esc 로 풀었다 — 기다리던 따라가기도 버린다(R61a 설치 전 검토 L1)
        if (key is { } && _board != null)
        {
            // 그 줄 몫만(통째로 그 줄인 판 + 덧그린 몫 — 이은 외벽 판 전체로 멀어지지 않게 · JACK 2026-10-23).
            var box = Box3.Empty;
            foreach (int piece in _fwLineWhole)
                if (FormworkShown(MemberCount + piece)) box = box.With(_board.Pieces[piece].Mesh.Box);
            for (int i = 0; i < _fwOverlay.Count; i++)
                if (FormworkShown(OverlayStart + i)) box = box.With(_fwOverlay[i].mesh.Box);
            if (!box.IsEmpty) _view3d.ZoomTo(box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
            // Revit 에서도 그 줄 몫의 부재(R61a — 거름으로 숨긴 판의 부재도 · 상자는 보이는 몫). 옮기기 갈래는 위에서 돌아갔다.
            FollowLater(OwnersOf(_board.PiecesOfRow(key.Value.type, key.Value.group, _fwPickedSub)), box);
        }
    }

    // 줄의 판들이 화면을 채우게: 둘레 여백은 조금만(Fit 의 margin 은 한쪽 비율 — 1.2 면 상자의 3.4 배로 멀어진다) · 작은 판 하나여도 둘레가 보이게 4 m 아래로는 좁히지 않는다.
    private const double RowZoomMargin = 0.15;
    private const double RowZoomMinWidth = 4;

    /// <summary>마우스 밑의 것(② ): 판이면 엑셀 줄 한 줄(절 · 부위 · 부호 · 식 · 넓이), 부재면 거푸집이 없는 면.</summary>
    private string DescribeFormwork(int index)
    {
        if (_board == null) return "";
        if (index < MemberCount)
        {
            var m = _members[index];
            return $"{m.Category} {m.Type} · Id {m.Id} · 거푸집을 대지 않는 면(윗면 · 다른 콘크리트가 맞닿은 자리 등)";
        }
        if (index >= OverlayStart) return OverlayPiece(index) is int under && under >= 0 ? PieceText(PieceAt(under)) : "";
        return PieceText(PieceAt(index));
    }

    // 덩이를 한 줄로(마우스 밑 딱지 · 찍은 판의 띠): 판 = «{절} · {부위}{ - 작은 제목} · {부호} ({괄호}) : {식} · {넓이} ㎡ · 부재 n»(엑셀 줄과 같은 말 — R48) ·
    // 지운 판 = «지운 판 · {종류} · {넓이} ㎡ · 부재 n» · 옮긴 판 = «옮김 → {줄} · » + 판의 글(R55b) · 판이 아닌 덩이(세지 않음 · 아직 정하지 않은 아래가 빈 밑면) = 유형 · 면 · 종류 · 넓이 · 까닭.
    private string PieceText(FormworkPiece piece)
    {
        int members = piece.Members.Select(m => m.Result.Element.Id).Distinct().Count();
        var lead = piece.Lead;
        if (piece.Dropped)
            return $"지운 판 · {(lead.Item.Length > 0 ? lead.Item + " · " : "")}{M2(piece.Area)} ㎡ · 부재 {members}" + (lead.Note == FormworkAnalyzer.OrphanNote ? " · " + lead.Note : "");
        string moved = (lead.KindLead.Length > 0 ? $"Revit 판 종류 {lead.Item} · " : "") + (lead.MovedTo is { } to ? $"옮김 → {RowLabel(to)} · " : "");
        if (piece.Plate is { } plate)
        {
            string title = plate.Part + (plate.Sub.Length > 0 ? " - " + plate.Sub : "");
            string tag = plate.Tag.Length > 0 ? $" ({plate.Tag})" : "";
            string check = lead.NeedsCheck ? " · 확인 필요" + (lead.Note.Length > 0 ? ": " + lead.Note : "") : "";
            return $"{moved}{plate.Section} · {title} · {plate.Mark}{tag} : {plate.Formula.Replace('*', '×')} · {M2(plate.Area)} ㎡ · 부재 {members}{check}";
        }
        if (moved.Length > 0) return $"{moved}{KindText(lead)} · {M2(piece.Area)} ㎡ · 부재 {members}" + (lead.Note.Length > 0 ? " · " + lead.Note : "");
        var el = piece.Result.Element;
        string side = piece.Side.Length > 0 ? $"({piece.Side})" : "";
        string mark = el.Mark.Length > 0 ? $" {el.Mark}" : "";
        string part = el.Part.Length > 0 ? $" · 부위 {el.Part}" : "";
        return $"{el.Category} {el.Type}{mark} · Id {el.Id}{part} · {piece.Group}{side} · {KindText(lead)} · {M2(piece.Area)} ㎡" +
               (lead.Note.Length > 0 ? " · " + lead.Note : "");
    }

    // ── 판 지우기 · 되살리기 · 옮기기 · 되돌리기(R48 · R55b — JACK 2026-10-21 «찍었을 때 지우기(Del 키도) · 되돌리기 · 지운 판을 수동으로 넣기» ·
    //    고름 «다른 줄로 옮기기» · «한 일 차례로 여러 번») ──
    // 판 하나의 상태 = 셈 ⇄ 지움 ⇄ 옮김(줄): 셈 → [지우기]/Del → 지움 · 지움 → [되살리기] → 셈 · 지움 → [옮기기] + 줄 → 옮김 ·
    // 옮김 → [지우기]/Del → 지움(기록은 그대로) · 옮김 → [원래 줄로] → 셈. 대응표를 모델에 적고(SaveRules) 덩이 · 줄 · 합계를 다시 센 뒤 겉면을 갈아 끼운다
    // (보는 자리는 그대로 · 찍은 판은 대표 조각으로 다시 찾아 그대로 고른 채). 한 일은 되돌리기 목록에 «그 판의 전 상태»로 남긴다(계획 검토 H3).

    // ② 에서 한 일(마지막이 끝) — 이름 한 줄 · 되돌리는 일(돌려주는 값 = 다시 셌다). 창을 닫으면 없어진다.
    private readonly List<(string what, Func<bool> undo)> _fwUndo = new();
    private string? _fwMoveLead;                 // 옮길 줄을 고르는 중인 판의 대표 열쇠(null = 아님)
    private Button? _fwMoveButton;               // [옮기기](지운 판) · [원래 줄로](옮긴 판)
    private Button? _fwKindClearButton;          // [원래 종류로](Revit 판 종류가 든 판 — R57 P3)

    private FormworkPiece? PickedPlatePiece() => _board != null && _fwPlate is { } lead && _board.IndexOf(lead) is int at && at >= 0 ? _board.Pieces[at] : null;

    // [지우기] · [되살리기](띠의 첫 단추): 세는 판 · 옮긴 판이면 지우기, 지운 판이면 되살리기.
    private bool PlateAction()
    {
        if (PickedPlatePiece() is not { } piece) return false;
        return piece.Dropped ? ChangePlate(piece, "되살리기", b => b.Restore(piece)) : DeletePiece(piece);
    }

    // Del · [지우기]: 세는 판 → 지움 · 옮긴 판 → 지움(옮긴 줄만 지운다 — 기록은 그대로) · 지운 판은 그대로(알림).
    private bool DeletePicked()
    {
        if (_board == null || Busy) return false;
        if (PickedPlatePiece() is not { } piece) { ShowMessage("지울 판을 3D 에서 먼저 찍어 주세요."); return false; }
        if (piece.Dropped) { ShowMessage("이미 지운 판입니다 — [되살리기] · [옮기기]"); return false; }
        return DeletePiece(piece);
    }

    private bool DeletePiece(FormworkPiece piece)
    {
        if (piece.Moved) return ChangePlate(piece, "지우기", b => b.SetMove(piece.MoveLead, null));
        if (piece.Plate == null) { ShowMessage("세지 않는 면이라 지울 판이 없습니다."); return false; }
        string dropped = "";
        return ChangePlate(piece, "지우기", b => b.Drop(piece, out dropped), () => dropped);
    }

    // [옮기기](지운 판 — 줄 고르기를 시작) · [원래 줄로](옮긴 판 — 셈).
    private bool MoveAction()
    {
        if (_board == null || Busy || PickedPlatePiece() is not { } piece) return false;
        if (piece.Moved) return ChangePlate(piece, "원래 줄로", b => b.Restore(piece));
        if (!piece.Dropped) return false;
        _fwMoveLead = piece.DropLead;
        Log?.Invoke($"거푸집 판 옮길 줄 고르기\t{PieceText(piece)}");
        RefreshAll();
        ShowMessage("옮길 줄을 표에서 누르세요 · Esc = 취소");
        return true;
    }

    // 옮길 줄 고르기를 그만둔다(고르는 중이었으면 true).
    private bool CancelMovePick()
    {
        if (_fwMoveLead == null) return false;
        _fwMoveLead = null;
        RefreshAll();
        ShowMessage("옮기기를 그만뒀습니다.");
        return true;
    }

    // 고른 줄로 옮긴다(표의 줄을 누름 — PickRow 가 부른다). 안 되는 줄이면 까닭만 알리고 계속 고른다.
    private void MoveTo((string type, string group) key)
    {
        if (_board == null || _fwMoveLead is not { } lead) return;
        int at = _board.IndexOfDrop(lead);
        if (at < 0) { _fwMoveLead = null; RefreshAll(); return; }
        var piece = _board.Pieces[at];
        string why = _board.WhyNotMoveTo(piece, key);
        if (why.Length > 0) { ShowMessage($"그 줄로는 옮길 수 없습니다({why}) — 다른 줄 · Esc = 취소", true); return; }
        _fwMoveLead = null;
        if (ChangePlate(piece, "옮기기 → " + RowLabel(key), b => b.SetMove(lead, key))) { _fwPicked = key; _fwPickedSub = null; }
        RefreshAll();
    }

    // 판의 상태를 바꾸고(act) · 되돌리기 목록에 그 판의 전 상태를 남기고 · 파일에 적고 · 다시 그린다. 바뀌지 않았으면 알리고 false.
    /// <param name="leadAfter">세는 판을 지울 때 적은 대표 열쇠(Board.Drop 이 돌려줌 — 설치 전 검토 L2). 없으면 판의 대표 열쇠.</param>
    private bool ChangePlate(FormworkPiece piece, string what, Func<FormworkBoard, bool> act, Func<string>? leadAfter = null)
    {
        if (_board == null || Busy || _fwPlate is not { } anchor) return false;
        string leadBefore = FormworkBoard.LeadOf(piece);
        var before = leadBefore.Length > 0 ? _board.StateOf(leadBefore) : null;
        double area = piece.Area;
        var ids = piece.Members.Select(m => m.Result.Element.Id).Distinct().ToList();
        string text = PieceText(piece);
        if (!act(_board))
        {
            // 열쇠가 판에 맞지 않아 바뀌지 않았다(revit-verify R48 설치 전 L4) — 되돌려져 있으니 알리고 일지에만 남긴다.
            Log?.Invoke($"거푸집 판 {what} 못 함\t{text} · 부재 Id {string.Join(",", ids.Take(8))}");
            ShowMessage($"바꾸지 못했습니다({what} — 판의 모양으로 자리를 찾지 못함) · 일지를 보내 주세요.", true);
            return false;
        }
        // 대표 열쇠: 지운 뒤에야 생긴다(셈 → 지움) — Board.Drop 이 적은 열쇠.
        string lead = leadBefore.Length > 0 ? leadBefore : leadAfter?.Invoke() ?? "";
        if (lead.Length > 0) _fwUndo.Add((what, () => { _board.SetState(lead, before); return true; }));
        FinishChange($"거푸집 판 {what}\t{text} · 부재 Id {string.Join(",", ids.Take(8))}{(ids.Count > 8 ? " …" : "")}",
            $"{what} · {M2(area)} ㎡ · 거푸집 {M2(_board.Total)} ㎡", what);
        return true;
    }

    // 다시 센 뒤: 고른 줄 = 찍은 판의 줄 · 겉면 갈아 끼우기 · 파일에 적기 · 일지 · 알림.
    private void FinishChange(string log, string said, string what)
    {
        if (_board == null) return;
        string problem = SaveRules?.Invoke() ?? "";
        var watch = Stopwatch.StartNew();
        // 새 줄을 먼저 정한 뒤 그린다 — 첫 그림부터 새 줄로(revit-verify R48 설치 전 L2). 시간은 다 그린 뒤까지 잰다.
        // 지운 판은 줄이 없다 — 고르던 줄은 그대로 둔다(R56a — JACK 2026-10-22 «지우면 선택한 객체만 보이고 다 사라진다»: 줄이 풀려 찍은 판만 진하게 남았다).
        if (_fwPlate is { } anchor && _board.IndexOf(anchor) is int now && now >= 0 && _board.RowOf(_board.Pieces[now]) is { } row)
        {
            if (_fwPicked != row) _fwPickedSub = null;
            _fwPicked = row;
        }
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke(log + $" · 지운 판 {_board.DroppedCount}장({M2(_board.DroppedArea)} ㎡) · 옮긴 판 {_board.MovedCount}장({M2(_board.MovedArea)} ㎡)" +
                    $" · 옮길 줄이 없어진 판 {_board.OrphanCount} · 모양이 바뀌어 다시 센 판 {_board.LostDrops.Count}" +
                    $" · 일부만 찾음 {_board.DropCheck.Partial.Count} · 넓이 달라짐 {_board.DropCheck.Resized.Count} · 거푸집 {M2(_board.Total)} ㎡" +
                    $" · 되돌릴 일 {_fwUndo.Count} · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        if (problem.Length > 0) ShowMessage($"{what} — 대응표를 모델에 적지 못했습니다(이 창에서는 바꾼 대로 셉니다): {problem}", true);
        else ShowMessage(said);
    }

    // [되돌리기] · Ctrl+Z(② — R55b): ② 에서 한 일을 마지막부터 하나씩(그 판의 전 상태 · 그 줄의 전 값을 다시 쓴다).
    private bool FormworkUndo()
    {
        if (_board == null || Busy) return false;
        if (_fwUndo.Count == 0) { ShowMessage("되돌릴 것이 없습니다."); return false; }
        var (what, undo) = _fwUndo[^1];
        _fwUndo.RemoveAt(_fwUndo.Count - 1);
        bool ok;
        try { ok = undo(); }
        catch (Exception ex)
        {
            // 되돌리지 못했다 — 항목은 남긴다(계획 검토 L4).
            _fwUndo.Add((what, undo));
            Log?.Invoke($"② 되돌리기 오류\t{what}\t{ex}");
            ShowMessage($"되돌리지 못했습니다({what}): {ex.Message}", true);
            return false;
        }
        if (!ok) { _fwUndo.Add((what, undo)); ShowMessage($"되돌리지 못했습니다({what}).", true); return false; }
        FinishChange($"② 되돌리기\t{what}", $"되돌림 — {what} · 거푸집 {M2(_board.Total)} ㎡", "되돌리기");
        return true;
    }

    // 줄의 짧은 이름(알림 · 띠): 부위 줄 = «부위 이름» · 유형 줄 = «유형 · 면».
    private static string RowLabel((string type, string group) key) =>
        FormworkRules.PartOf(key.type) is { } part ? $"부위 {part}" : $"{key.type} · {key.group}";

    private string KindText(FacePatch patch) =>
        patch.Counted ? $"{patch.Item} {(patch.Item == FormworkTable.Euro && _board != null ? FormworkTable.BandName(patch.Band, _board.Options.Bands) : "")}".TrimEnd()
        : patch.NeedsCheck ? "확인 필요" : FormworkRules.NotCounted;

    // ── 왼쪽 판: 대응표 ─────────────────────────────────────────────────────────

    private FrameworkElement BuildFormworkSide()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var columns = RowGrid();
        columns.Margin = new Thickness(8, 0, 4, 4);
        var titles = new[] { ("면", HorizontalAlignment.Left), ("넓이(㎡)", HorizontalAlignment.Right), ("종류", HorizontalAlignment.Left) };
        for (int c = 0; c < titles.Length; c++)
        {
            var label = new TextBlock { Text = titles[c].Item1, Foreground = _sub, FontSize = 12, HorizontalAlignment = titles[c].Item2, Margin = new Thickness(c == 1 ? 0 : c == 2 ? 2 : 0, 0, c == 1 ? 10 : 0, 0) };
            Grid.SetColumn(label, c);
            columns.Children.Add(label);
        }
        DockPanel.SetDock(columns, Dock.Top);
        dock.Children.Add(columns);
        var list = new StackPanel();
        list.Children.Add(_fwRows);
        list.Children.Add(_fwDropRows);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        return dock;
    }


    // 덧그림을 짓는 기준(② 대응표 줄의 열쇠 · ③ 거푸집 줄) — RefreshFormwork · RefreshReview 가 바뀐 것을 보고 겉면을 갈아 끼운다.
    private object? OverlayKey() => Step == 2 ? _rvFormLine : _fwPicked is { } row ? (row, _fwPickedSub) : null;


    /// <summary>시험용: 덧그린 줄 몫 수 · 통째로 고른 줄인 덩이.</summary>
    public int OverlayCount => _fwOverlay.Count;
    public IReadOnlyCollection<int> LineWholePieces => _fwLineWhole;

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // 꼬리표 «자동» · «직접 ↺»(JACK 2026-10-21)
        return grid;
    }

    // 줄들: 유형마다 머리(카테고리 · 유형 · 부재 수) + 면의 갈래마다 한 줄([면][넓이][종류 ▾]). 줄의 차례는 대응표를 고쳐도 바뀌지 않는다.
    // 맨 위의 부위 줄(R47)은 머리가 «부위  이름  n개»(n = 그 부위를 넣은 부재 수)이고, 줄은 «모든 면(구멍 둘레 빼고)» 하나다.
    private void BuildFormworkRows()
    {
        _fwRows.Children.Clear();
        _fwRowViews.Clear();
        _fwSubViews.Clear();
        _fwSubRows.Clear();
        _fwDropKey = null;                        // 지운 판 묶음도 새 판으로 다시 짓는다(RefreshDroppedRows — 새 판에 지운 판이 없어도 옛 줄을 지운다 · 설치 전 검토 M1)
        if (_board == null) return;
        if (_board.Rows.Count == 0)
        {
            _fwRows.Children.Add(Note("거푸집으로 볼 면이 없습니다."));
            return;
        }
        string? lastType = null;
        // 버림의 면이 든 줄(R48 — 안쪽 면은 초안에서 세지 않는다): 고르는 칸의 풍선 글에 적는다(core-verify R48 계획 L4).
        var blinding = FormworkSummary.RowPatches(_board.Results).Where(k => k.Result.Element.Blinding).Select(FormworkSummary.RowOf).ToHashSet();
        foreach (var row in _board.Rows)
        {
            if (row.Type != lastType)
            {
                lastType = row.Type;
                string? part = FormworkRules.PartOf(row.Type);
                int count = part != null ? _board.Results.Count(r => r.Element.Part == part) : _board.Results.Count(r => r.Element.Type == row.Type);
                var title = new TextBlock { Margin = new Thickness(2, _fwRows.Children.Count == 0 ? 2 : 10, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                title.Inlines.Add(new Run(row.Category + "  ") { Foreground = _sub, FontSize = 12 });
                title.Inlines.Add(new Run(part ?? row.Type) { FontWeight = FontWeights.SemiBold });
                title.Inlines.Add(new Run($"  {count}개") { Foreground = _sub, FontSize = 12 });
                _fwRows.Children.Add(title);
            }
            var key = (row.Type, row.Group);
            var grid = RowGrid();
            // 부위 줄의 «모든 면(구멍 둘레 빼고)»는 좁은 칸에서 잘리지 않게 두 줄로 접는다(«구멍 둘레 빼고»가 잘리면 구멍 둘레도 바뀌는 줄로 읽힌다).
            bool partRow = FormworkRules.PartOf(row.Type) != null;
            var group = new TextBlock
            {
                Text = row.Group, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = partRow ? TextTrimming.None : TextTrimming.CharacterEllipsis, TextWrapping = partRow ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
            var area = new TextBlock { Text = M2(row.Area), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var combo = KindCombo(row, blinding.Contains(key));
            // 닫힌 칸 왼쪽의 색 네모(3D 의 그 종류 색 — 칸 위에 얹는다 · 누름은 칸이 받는다).
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(9, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var kindCell = new Grid();
            kindCell.Children.Add(combo);
            kindCell.Children.Add(swatch);
            var tag = new TextBlock { FontSize = 11.5, Foreground = _sub, VerticalAlignment = VerticalAlignment.Center };
            var reset = _ui.Button("↺", "BtnGhost", "자동으로 — 프로그램 규칙의 종류로 돌립니다.", $"fw.reset.{row.Type}.{row.Group}");
            reset.Focusable = false;
            reset.MinHeight = 22;
            reset.Padding = new Thickness(5, 0, 5, 1);
            reset.Margin = new Thickness(2, 0, 0, 0);
            reset.Click += (_, _) => ChangeRule(key, "");
            var tagCell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tagCell.Children.Add(tag);
            tagCell.Children.Add(reset);
            _ui.Mark(tag, "", $"fw.tag.{row.Type}.{row.Group}");
            Grid.SetColumn(area, 1);
            Grid.SetColumn(kindCell, 2);
            Grid.SetColumn(tagCell, 3);
            grid.Children.Add(group);
            grid.Children.Add(area);
            grid.Children.Add(kindCell);
            grid.Children.Add(tagCell);
            var line = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 4, 3), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _ui.Mark(line, "", $"fw.row.{row.Type}.{row.Group}");
            line.MouseLeftButtonUp += (_, e) => { if (!Within(e.OriginalSource as DependencyObject, combo) && !Within(e.OriginalSource as DependencyObject, reset)) PickRow(key); };
            _fwRows.Children.Add(line);
            _fwRowViews[key] = (line, combo, area, swatch, tag, reset);
            // 아래 줄(R64): 지금 «여러 종류»이거나 아래 줄 값이 있는 줄만 — 보드 동안 그대로.
            if ((row.Subs.Count >= 2 && row.Draft == FormworkSummary.Mixed) || row.Subs.Any(s => s.Rule.Length > 0))   // 아래 줄 값이 있으면 늘(말없이 걸리지 않게 — 설치 전 검토 낮음)
            {
                _fwSubRows.Add(key);
                foreach (var sub in row.Subs) AddSubLine(row, sub);
            }
        }
    }

    // 아래 줄 한 줄: «└ 벽과 한 덩어리» · 넓이 · 종류 칸(첫 줄 = 줄 값이 있으면 «줄 따름(…)», 없으면 «자동(…)») · 꼬리표 · ↺.
    private void AddSubLine(FormworkRow row, FormworkSubRow sub)
    {
        var key = (row.Type, row.Group);
        string subKey = sub.Key;
        var grid = RowGrid();
        var name = new TextBlock { Text = "└ " + SubLabel(row, sub), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = _sub, Margin = new Thickness(4, 0, 0, 0) };
        _ui.Tip(name, SubTip(row, sub));
        var area = new TextBlock { Text = M2(sub.Area), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = _sub };
        var combo = _ui.Combo($"{ScopeText(row)} 가운데 «{SubLabel(row, sub)}» 판에만 적용됩니다.", $"fw.kind.{row.Type}.{row.Group}.{subKey}");
        combo.MinHeight = 24;
        combo.FontSize = 12;
        combo.Padding = new Thickness(25, 0, 26, 1);
        combo.Items.Add(new ComboBoxItem { Content = SubAuto(row, sub), Tag = "", ContentTemplate = AutoTemplate() });
        bool banded = row.Group is not (FormworkTable.GroupSlope or FormworkTable.GroupHole);
        foreach (string item in FormworkTable.Items)
            if (banded || item != FormworkTable.Euro) combo.Items.Add(KindItem(item, item));
        if (sub.Rule.Length > 0 && !combo.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == sub.Rule)) combo.Items.Add(KindItem(sub.Rule, sub.Rule));
        SelectKind(combo, sub.Rule);
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedItem is not ComboBoxItem { Tag: string choice }) return;
            combo.Dispatcher.BeginInvoke(() => ChangeSubRule(key, subKey, choice));
        };
        combo.PreviewMouseWheel += (_, e) => PassWheel(combo, e);
        combo.DropDownClosed += (_, _) => _view3d.Focus();
        var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(9, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var kindCell = new Grid();
        kindCell.Children.Add(combo);
        kindCell.Children.Add(swatch);
        var tag = new TextBlock { FontSize = 11.5, Foreground = _sub, VerticalAlignment = VerticalAlignment.Center };
        var reset = _ui.Button("↺", "BtnGhost", "이 아래 줄을 줄 값(없으면 자동)으로 돌립니다.", $"fw.reset.{row.Type}.{row.Group}.{subKey}");
        reset.Focusable = false;
        reset.MinHeight = 22;
        reset.Padding = new Thickness(5, 0, 5, 1);
        reset.Margin = new Thickness(2, 0, 0, 0);
        reset.Click += (_, _) => ChangeSubRule(key, subKey, "");
        var tagCell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        tagCell.Children.Add(tag);
        tagCell.Children.Add(reset);
        Grid.SetColumn(area, 1);
        Grid.SetColumn(kindCell, 2);
        Grid.SetColumn(tagCell, 3);
        grid.Children.Add(name);
        grid.Children.Add(area);
        grid.Children.Add(kindCell);
        grid.Children.Add(tagCell);
        var line = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 1, 4, 1), Margin = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _ui.Mark(line, "", $"fw.sub.{row.Type}.{row.Group}.{subKey}");
        line.MouseLeftButtonUp += (_, e) => { if (!Within(e.OriginalSource as DependencyObject, combo) && !Within(e.OriginalSource as DependencyObject, reset)) PickRow(key, subKey); };
        _fwRows.Children.Add(line);
        _fwSubViews[(row.Type, row.Group, subKey)] = (line, combo, area, swatch, tag, reset);
    }

    // 아래 줄 이름표(좁은 칸 — 짧게 · 풀이는 풍선 글): 덩어리 → «벽 덩어리» · «슬래브 덩어리» · 나머지가 하나면 «나머지», 여럿이면 «그 밖(자동 종류)».
    private static string SubLabel(FormworkRow row, FormworkSubRow sub)
    {
        if (sub.Key == FormworkAnalyzer.LumpWall) return "벽 덩어리";
        if (sub.Key == FormworkAnalyzer.LumpSlab) return "슬래브 덩어리";
        return row.Subs.Count(s => s.Key is not (FormworkAnalyzer.LumpWall or FormworkAnalyzer.LumpSlab)) == 1 ? "나머지" : $"그 밖({ClosedDraft(sub.Key)})";
    }

    private static string SubTip(FormworkRow row, FormworkSubRow sub) =>
        sub.Key == FormworkAnalyzer.LumpWall ? "벽과 한 평면으로 이어져 한 덩어리가 된 판 — 자동이면 벽의 종류를 따릅니다."
        : sub.Key == FormworkAnalyzer.LumpSlab ? "슬래브와 한 평면으로 이어져 한 덩어리가 된 판 — 자동이면 슬래브의 종류를 따릅니다."
        : $"이 줄의 나머지 판 — 자동이면 {ClosedDraft(sub.Key)}.";

    // 아래 줄의 첫 칸: 줄 값이 있으면 그 값을 따른다(«줄 따름(…)» — critic R64 M3), 없으면 그 아래 줄의 자동 종류.
    private static KindAuto SubAuto(FormworkRow row, FormworkSubRow sub) =>
        row.Rule.Length > 0 ? new KindAuto(row.Rule, $"줄 따름({row.Rule})") : new KindAuto(ClosedDraft(sub.Draft), OpenDraft(sub.Draft));

    // 아래 줄의 종류를 바꿨다(R64): 그 아래 줄만 적는다(줄 값 그대로 — critic R64 H1). 되돌리기 = 그 줄의 값 전부(줄 + 아래 줄) 스냅숏.
    private bool ChangeSubRule((string type, string group) key, string sub, string choice)
    {
        if (_board == null || Busy) return false;
        var before = _board.RowRulesOf(key.type, key.group);
        if (!_board.SetSubRule(key.type, key.group, sub, choice)) return false;
        var label = RowOf(key) is { } r && r.Subs.FirstOrDefault(s => s.Key == sub) is { } s ? SubLabel(r, s) : sub;
        string name = choice.Length > 0 ? choice : "줄 값 · 자동";
        _fwUndo.Add(($"{RowLabel(key)} · {label} → {name}", () => { _board.SetRowRules(key.type, key.group, before); return true; }));
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        _fwPickedSub = sub;
        if (_fwPlate is { } plate && (_board.IndexOf(plate) is var at && (at < 0 || !_board.InRow(_board.Pieces[at], key.type, key.group, sub)))) _fwPlate = null;
        var watch = Stopwatch.StartNew();
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke($"거푸집 대응표\t{key.type} · {key.group} · {sub} → {name} · 판 {_board.Pieces.Count(p => p.Plate != null)} · 거푸집 {M2(_board.Total)} ㎡" +
                    $" · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        if (problem.Length > 0)
            ShowMessage($"{RowLabel(key)} · {label} → {name} — 대응표를 모델에 적지 못했습니다(이 창에서는 바꾼 값으로 셉니다): {problem}", true);
        else ShowMessage($"{RowLabel(key)} · {label} → {name} · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    // 찍은 판이 든 아래 줄(그 줄이 아래 줄을 보일 때만 — 판 띠의 종류 단추가 그 아래 줄을 바꾼다 · critic R64 M4).
    private string? PickedPlateSub((string type, string group) key) =>
        _board != null && _fwSubRows.Contains(key) && _fwPickedIndex >= 0 && PieceAt(_fwPickedIndex) is { Dropped: false } piece ? _board.SubOf(piece) : null;

    // 종류 칸(JACK 2026-10-21 «처음 보는 사람도 알기 쉽게» · 고름 «자동/직접 표시» · «선택 필요처럼 간결하게»): [자동(규칙의 종류)] · 유로폼 · 합판 3회 · 합판 4회 · 합판 6회 · 세지 않음.
    // 닫힌 칸은 종류만(자동이면 규칙의 종류 · 정할 것이면 «⚠ 선택 필요») — 펼치면 첫 줄이 «자동(…)» · 칸 옆 꼬리표가 자동 / 직접 ↺.
    // 높이 구간으로 자르지 않는 면(위를 보는 경사면 · 구멍 둘레)에는 유로폼을 두지 않는다(R42 core 낮음). 파일에 적힌 낯선 값도 그대로 보인다.
    private ComboBox KindCombo(FormworkRow row, bool blinding)
    {
        string tip = FormworkRules.PartOf(row.Type) != null
            ? "이 부위의 모든 면(구멍 둘레 · 아래가 빈 밑면 빼고)에 적용됩니다."
            : "이 유형의 이 면에 적용됩니다(부위를 넣은 부재의 면은 부위 줄에서 정합니다).";
        if (blinding) tip += " 버림의 안쪽 면(호퍼 · 피트 둘레)은 자동으로는 세지 않습니다 — 종류를 고르면 다시 셉니다.";
        var combo = _ui.Combo(tip, $"fw.kind.{row.Type}.{row.Group}");
        combo.MinHeight = 26;
        combo.FontSize = 12.5;
        combo.Padding = new Thickness(25, 0, 26, 1);
        combo.Items.Add(AutoItem(row));
        bool banded = row.Group is not (FormworkTable.GroupSlope or FormworkTable.GroupHole);
        foreach (string item in FormworkTable.Items)
            if (banded || item != FormworkTable.Euro) combo.Items.Add(KindItem(item, item));
        if (row.Rule.Length > 0 && !combo.Items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == row.Rule)) combo.Items.Add(KindItem(row.Rule, row.Rule));
        SelectKind(combo, row.Rule);
        var key = (row.Type, row.Group);
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedItem is not ComboBoxItem { Tag: string choice }) return;
            // 고르는 칸의 처리가 끝난 뒤에 다시 센다(처리 중에 줄들의 값을 다시 맞추면 고르던 칸이 흔들린다).
            combo.Dispatcher.BeginInvoke(() => ChangeRule(key, choice));
        };
        // 닫혀 있는 칸 위에서 굴리면 종류가 바뀌지 않고 목록이 굴러간다(판을 굴리던 손이 칸 위를 지나도).
        combo.PreviewMouseWheel += (_, e) => PassWheel(combo, e);
        combo.DropDownClosed += (_, _) => _view3d.Focus();
        return combo;
    }

    /// <summary>종류 칸의 자동 줄 글감: 닫힌 칸 = Closed(종류만) · 펼친 목록 = Open(«자동(…)»).</summary>
    public sealed record KindAuto(string Closed, string Open)
    {
        public override string ToString() => Closed;
    }

    // 자동 줄의 글 틀: 목록 안(ComboBoxItem 안)이면 Open, 닫힌 칸이면 Closed — 닫힌 칸은 고른 줄의 글감과 틀을 그대로 쓰고 ComboBoxItem 조상이 없다.
    // 펼침 · 닫힘 알림으로 글을 바꾸지 않는다(닫힘 알림은 목록이 서서히 사라진 뒤에야 오고, 고른 줄의 글을 바꾸면 닫힌 칸도 바로 따라 바뀐다 — 하네스 1021).
    private DataTemplate? _autoTemplate;
    private DataTemplate AutoTemplate() => _autoTemplate ??= (DataTemplate)XamlReader.Parse(
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
        "<TextBlock x:Name=\"t\" Text=\"{Binding Closed}\"/>" +
        "<DataTemplate.Triggers>" +
        "<DataTrigger Binding=\"{Binding IsEnabled, RelativeSource={RelativeSource AncestorType=ComboBoxItem}, FallbackValue=False}\" Value=\"True\">" +
        "<Setter TargetName=\"t\" Property=\"Text\" Value=\"{Binding Open}\"/>" +
        "</DataTrigger>" +
        "</DataTemplate.Triggers>" +
        "</DataTemplate>");

    private ComboBoxItem AutoItem(FormworkRow row) =>
        new() { Content = new KindAuto(ClosedDraft(row.Draft), OpenDraft(row.Draft)), Tag = "", ContentTemplate = AutoTemplate() };

    // 규칙의 종류가 바뀌었으면(판을 빼거나 붙은 벽이 바뀜 — R47 · R48) 자동 줄의 글감을 갈아 끼운다(고른 줄이면 닫힌 칸도 따라 바뀐다).
    private static void SyncAuto(ComboBox combo, FormworkRow row)
    {
        if (combo.Items.Count == 0 || combo.Items[0] is not ComboBoxItem { Tag: "" } auto) return;
        var now = new KindAuto(ClosedDraft(row.Draft), OpenDraft(row.Draft));
        if (!Equals(auto.Content, now)) auto.Content = now;
    }

    // 자동 줄의 글: 닫힌 칸 = 규칙의 종류(정할 것 «⚠ 선택 필요» · 여러 종류 · 뺀 판) · 펼친 목록 = «자동(…)».
    private static string ClosedDraft(string draft) => draft switch
    {
        FormworkSummary.Ask => "⚠ 선택 필요",
        FormworkSummary.Mixed => "여러 종류",
        FormworkSummary.Dropped => "지운 판",
        _ => draft,
    };

    private static string OpenDraft(string draft) => "자동(" + draft switch
    {
        FormworkSummary.Ask => "선택 필요",
        FormworkSummary.Mixed => "여러 종류",
        FormworkSummary.Dropped => "지운 판",
        _ => draft,
    } + ")";

    private FormworkRow? RowOf((string type, string group) key) => _board?.Rows.FirstOrDefault(r => r.Type == key.type && r.Group == key.group);

    // 줄의 색 네모: 지금 걸린 종류의 3D 색(정할 것 = 분홍 · 뺀 판 = 회갈색 · 여러 종류 = 없음).
    private static (byte r, byte g, byte b)? SwatchOf(FormworkRow row)
    {
        string kind = row.Rule.Length > 0 ? row.Rule : row.Draft;
        return kind switch
        {
            FormworkSummary.Ask => FormworkPalette.Check,
            FormworkSummary.Dropped => FormworkPalette.Dropped,
            FormworkSummary.Mixed or FormworkSummary.PlateKinds => null,
            _ => FormworkPalette.Base(kind),
        };
    }

    private static ComboBoxItem KindItem(string value, string label) => new() { Content = label, Tag = value };

    private static void SelectKind(ComboBox combo, string rule)
    {
        var want = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == rule) ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        if (!ReferenceEquals(combo.SelectedItem, want)) combo.SelectedItem = want;
    }

    private static void PassWheel(ComboBox combo, MouseWheelEventArgs e)
    {
        if (combo.IsDropDownOpen) return;
        e.Handled = true;
        if (combo.Parent is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = combo });
    }

    // 종류를 바꿨다: 대응표만 다시 걸고(형상은 그대로) 파일에 적은 뒤 화면을 맞춘다. 그 줄이 골라진다(3D 에서 바뀐 면이 보이게).
    // 판이 이어지거나 끊길 수 있어(종류가 같아야 잇는다) 겉면을 갈아 끼운다(R48 — 보는 자리 · 찍은 판은 그대로).
    private bool ChangeRule((string type, string group) key, string choice, bool record = true)
    {
        if (_board == null || Busy) return false;
        var before = _board.RowRulesOf(key.type, key.group);
        if (!_board.SetRule(key.type, key.group, choice)) return false;
        // 되돌리기 목록(R55b): 그 줄의 전 값 — 아래 줄 값까지(R64 — 줄에서 고르면 아래 줄 값을 지우므로 스냅숏으로 되살린다).
        if (record) _fwUndo.Add(($"{RowLabel(key)} → {(choice.Length > 0 ? choice : "자동")}", () => { _board.SetRowRules(key.type, key.group, before); return true; }));
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        _fwPickedSub = null;
        // 찍어 둔 판이 바꾼 줄의 판이 아니면 푼다 — 흰 막 · 띠가 다른 줄의 판에 남지 않게(revit-verify R48 설치 전 L3).
        if (_fwPlate is { } plate && (_board.IndexOf(plate) is var at && (at < 0 || !_board.InRow(_board.Pieces[at], key.type, key.group)))) _fwPlate = null;
        var watch = Stopwatch.StartNew();
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke($"거푸집 대응표\t{key.type} · {key.group} → {(choice.Length > 0 ? choice : "자동")} · 판 {_board.Pieces.Count(p => p.Plate != null)} · 거푸집 {M2(_board.Total)} ㎡" +
                    $" · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        string name = choice.Length > 0 ? choice : "자동";
        string row = RowLabel(key);
        if (problem.Length > 0)
            ShowMessage($"{row} → {name} — 대응표를 모델에 적지 못했습니다(이 창에서는 바꾼 값으로 셉니다): {problem}", true);
        else ShowMessage($"{row} → {name} · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    // [원래 종류로](R57 P3): 찍은 판의 판 종류 기록을 걷는다 — 되돌리기는 그 기록을 그 차례에 다시 쓴다(계획 검토 M4).
    private bool ClearPlateKind()
    {
        if (_board == null || Busy || PickedPlatePiece() is not { Dropped: false } piece || piece.KindLead is not { Length: > 0 } lead) return false;
        var before = _board.KindStateOf(lead);
        double area = piece.Area;
        string text = PieceText(piece);
        if (!_board.ClearKind(piece)) return false;
        _fwUndo.Add(("원래 종류로", () => { _board.SetKindState(lead, before); return true; }));
        FinishChange($"거푸집 판 원래 종류로\t{text}", $"원래 종류로 · {M2(area)} ㎡ · 거푸집 {M2(_board.Total)} ㎡", "원래 종류로");
        return true;
    }

    // 모양이 바뀌어 찾지 못한 뺀 판의 기록을 지운다(revit-verify R48 설치 전 M1): 대응표를 모델에 적고 화면을 맞춘다(ChangeRule 과 같은 길).
    private bool ForgetLostDrops()
    {
        if (_board == null || Busy) return false;
        var leads = _board.LostDrops.ToList();
        var states = leads.Select(l => (l, _board.StateOf(l))).ToList();
        // 찾지 못한 Revit 판 종류 기록도(R57 P3 — 계획 검토 L9) · 차례까지 되돌린다(앞의 것부터 제자리에).
        var kindStates = _board.KindCheck.Lost.Select(l => (l, state: _board.KindStateOf(l))).OrderBy(x => x.state?.Order ?? 0).ToList();
        int n = _board.ForgetLostDrops() + _board.ForgetLostKinds();
        if (n == 0) return false;
        _fwUndo.Add(($"찾지 못한 판 기록 지우기 {n}장", () =>
        {
            _board.SetStates(states);
            foreach (var (lead, state) in kindStates) _board.SetKindState(lead, state);
            return true;
        }));
        string problem = SaveRules?.Invoke() ?? "";
        var watch = Stopwatch.StartNew();
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke($"찾지 못한 뺀 판 기록 지움\t{n}장 · " + string.Join(" / ", leads.Take(4)) + (leads.Count > 4 ? " …" : "") +
                    $" · 거푸집 {M2(_board.Total)} ㎡ · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        if (problem.Length > 0)
            ShowMessage($"찾지 못한 뺀 판 기록 {n}장을 지웠지만 모델에 적지 못했습니다: {problem}", true);
        else ShowMessage($"찾지 못한 뺀 판 기록 {n}장을 지웠습니다 · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    // ── 그림 위 · 아래 ───────────────────────────────────────────────────────────

    // 그림 위: 거푸집 합계 · 확인할 자리 + 종류별 넓이(딱지) + 찍은 판의 띠(엑셀 줄 한 줄 · [이 판 빼기] / [되살리기] — 찍었을 때만).
    private FrameworkElement BuildFormworkTop()
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_fwTotal);
        _ui.Mark(_fwWarnings, "", "fw.warnings");
        texts.Children.Add(_fwWarnings);
        texts.Children.Add(_fwKinds);
        _fwPlateButton = _ui.Button("지우기", "BtnSoft", "찍은 판을 지웁니다(Del) — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다(회갈색 · [되살리기] · [옮기기]).", "btn.fw.drop");
        _fwPlateButton.Margin = new Thickness(6, 0, 0, 0);
        // 눌러도 글자판을 가져가지 않는다 — 가져가면 다음 Enter · Space 가 말없이 [되살리기]를 누른다(revit-verify R48 설치 전 L1).
        _fwPlateButton.Focusable = false;
        _fwPlateButton.Click += (_, _) => PlateAction();
        // [옮기기](지운 판 — 표에서 줄을 누르면 그 줄 · 그 종류로 셈) · [원래 줄로](옮긴 판) — R55b(JACK 2026-10-21 고름 «다른 줄로 옮기기»).
        _fwMoveButton = _ui.Button("옮기기", "BtnSoft", "지운 판을 표의 다른 줄로 옮겨 그 줄 · 그 종류로 셉니다 — 누른 뒤 표에서 줄을 누르세요(Esc = 취소).", "btn.fw.move");
        _fwMoveButton.Margin = new Thickness(6, 0, 0, 0);
        _fwMoveButton.Focusable = false;
        _fwMoveButton.Click += (_, _) => MoveAction();
        // [원래 종류로](R57 P3 — Revit 판의 종류 칸으로 정한 판): 그 판의 판 종류를 걷어 줄의 종류로 셈(Ctrl+Z 로 되돌림).
        _fwKindClearButton = _ui.Button("원래 종류로", "BtnSoft", "Revit 판에서 정한 종류를 걷고 표의 줄 종류로 셉니다.", "btn.fw.kindClear");
        _fwKindClearButton.Margin = new Thickness(6, 0, 0, 0);
        _fwKindClearButton.Focusable = false;
        _fwKindClearButton.Click += (_, _) => ClearPlateKind();
        var bar = new DockPanel();
        DockPanel.SetDock(_fwKindClearButton, Dock.Right);
        bar.Children.Add(_fwKindClearButton);
        DockPanel.SetDock(_fwPlateButton, Dock.Right);
        bar.Children.Add(_fwPlateButton);
        DockPanel.SetDock(_fwMoveButton, Dock.Right);
        bar.Children.Add(_fwMoveButton);
        _ui.Mark(_fwPlateText, "", "fw.plate");
        bar.Children.Add(_fwPlateText);
        // 둘째 줄: 그 판의 줄(대응표와 같은 범위 — 유형 × 면 · 부위 줄이면 그 부위 전체)의 종류 단추 · 자동으로(↺) · 범위(JACK 2026-10-21 «3D 에서 찍어서 하는 기능도»).
        foreach (string item in FormworkTable.Items.Where(i => i != FormworkRules.NotCounted))
        {
            var kind = _ui.Button(item, "BtnSecondary", "", "btn.fw.kind." + item);
            kind.Focusable = false;
            kind.MinHeight = 26;
            kind.Padding = new Thickness(9, 0, 9, 1);
            kind.Margin = new Thickness(0, 0, 4, 0);
            string choice = item;
            kind.Click += (_, _) =>
            {
                if (PickedPlateRow() is not { } key) return;
                if (PickedPlateSub(key) is { } sub) ChangeSubRule(key, sub, choice);   // 아래 줄을 보이는 줄이면 그 아래 줄만(R64)
                else ChangeRule(key, choice);
            };
            _fwKindButtons[item] = kind;
            _fwKindBar.Children.Add(kind);
        }
        _fwKindAuto = _ui.Button("↺ 자동", "BtnGhost", "자동으로 — 프로그램 규칙의 종류로 돌립니다.", "btn.fw.kind.auto");
        _fwKindAuto.Focusable = false;
        _fwKindAuto.MinHeight = 26;
        _fwKindAuto.Padding = new Thickness(8, 0, 8, 1);
        _fwKindAuto.Click += (_, _) =>
        {
            if (PickedPlateRow() is not { } key) return;
            if (PickedPlateSub(key) is { } sub) ChangeSubRule(key, sub, "");
            else ChangeRule(key, "");
        };
        _fwKindBar.Children.Add(_fwKindAuto);
        _fwKindScope.Foreground = _sub;
        _ui.Mark(_fwKindScope, "", "fw.kind.scope");
        _fwKindBar.Children.Add(_fwKindScope);
        var plateLines = new StackPanel();
        plateLines.Children.Add(bar);
        plateLines.Children.Add(_fwKindBar);
        _fwPlateBar = new Border
        {
            Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 6, 0, 0),
            Child = plateLines, Visibility = Visibility.Collapsed,
        };
        texts.Children.Add(_fwPlateBar);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = texts,
        };
    }

    // 그림 아래: 색 풀이 · 마우스 쓰는 법.
    private FrameworkElement BuildFormworkBottom(string? hintText = null)
    {
        var box = new DockPanel { Margin = new Thickness(2, 10, 2, 0) };
        var hint = new TextBlock
        {
            Text = hintText ?? "굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기 · 판을 찍으면 그 줄 · Del = 지우기 · Ctrl+Z = 되돌리기",
            Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(hint, Dock.Right);
        box.Children.Add(hint);
        var legend = new WrapPanel();
        void Item((byte r, byte g, byte b) c, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 2) };
            item.Children.Add(Swatch(c.r, c.g, c.b));
            item.Children.Add(new TextBlock { Text = text, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(item);
        }
        Item(FormworkPalette.Of(new FacePatch { Item = FormworkTable.Euro }), FormworkTable.Euro);
        // 높은 구간의 색은 높이 구간을 켰을 때만(기본은 7 m 구분 없음 — R46).
        if (_board is { Options.Bands.Count: > 0 }) Item(FormworkPalette.Of(new FacePatch { Item = FormworkTable.Euro, Band = 1 }), "유로폼(높은 구간)");
        Item(FormworkPalette.Base(FormworkTable.Ply3), FormworkTable.Ply3);
        Item(FormworkPalette.Base(FormworkTable.Ply4), FormworkTable.Ply4);
        Item(FormworkPalette.Base(FormworkTable.Ply6), FormworkTable.Ply6);
        Item(FormworkPalette.Check, "확인할 자리");
        Item(FormworkPalette.Dropped, "지운 판");
        legend.Children.Add(new TextBlock { Text = "연한 색 = 안쪽 면 · 흰 막 = 찍은 판", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
        box.Children.Add(legend);
        return box;
    }

    // ② 의 화면을 지금 값으로 맞춘다: 줄의 바탕 · 넓이 · 초안 · 종류 칸 · 합계 · 아래 줄의 글.
    // 줄의 넓이와 초안은 바뀔 수 있다(판을 빼면 그 조각이 넓이에서 빠진다 · 붙은 벽의 종류가 바뀐다 — R47 · R48). 줄의 모임은 그대로다.
    private void RefreshFormwork()
    {
        if (_board == null) return;
        // 고른 줄이 바뀌었으면 덧그림을 다시 짓는다(겉면 갈아 끼우기 — 대응표 줄을 누르거나 풀 때) · 아니면 모습만.
        if (!Equals(OverlayKey(), _fwOverlayFor)) SetFormworkMeshes(selectionOnly: true);
        else UpdateFormworkLook();
        // 옮길 줄 고르기는 그 지운 판을 찍고 있을 때만(판이 풀리거나 판을 다시 지어 바뀌면 그만둔다 — 줄을 눌러도 아무 일 없는 상태가 남지 않게 · 설치 전 검토 L1).
        if (_fwMoveLead != null && !(PickedPlatePiece() is { Dropped: true } movePiece && movePiece.DropLead == _fwMoveLead)) _fwMoveLead = null;
        RefreshDroppedRows();
        _syncing = true;
        try
        {
            foreach (var row in _board.Rows)
            {
                if (!_fwRowViews.TryGetValue((row.Type, row.Group), out var view)) continue;
                bool picked = _fwPicked == (row.Type, row.Group) && _fwPickedSub == null;
                bool ask = row.Draft == FormworkSummary.Ask && row.Rule.Length == 0;   // 아직 정하지 않은 '아래가 빈 밑면'
                view.row.Background = picked ? _ui["AccentSoft"] : ask ? _ui["WarnBg"] : Brushes.Transparent;
                view.area.Text = M2(row.Area);
                SyncAuto(view.combo, row);
                SelectKind(view.combo, row.Rule);
                // 색 네모 · 꼬리표: 자동(회색) · 직접 + ↺ · 정할 것 · 뺀 판은 꼬리표 없음(고를 것이 없다 — 판을 되살리면 그 종류).
                var c = SwatchOf(row);
                view.swatch.Visibility = c == null ? Visibility.Hidden : Visibility.Visible;
                if (c is { } rgb) view.swatch.Background = new SolidColorBrush(Color.FromRgb(rgb.r, rgb.g, rgb.b));
                bool direct = row.Rule.Length > 0;
                view.tag.Text = direct ? "직접" : row.Draft is FormworkSummary.Ask or FormworkSummary.Dropped or FormworkSummary.PlateKinds ? "" : "자동";
                view.tag.Foreground = direct ? _ui["AccentText"] : _sub;
                view.reset.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
                // 아래 줄(R64): 아래 줄 값이 있으면 부모도 «직접» + ↺(누르면 줄 · 아래 줄 값을 모두 자동으로 — 꼬리표 칸이 좁아 짧은 글).
                bool subRules = row.Subs.Any(s => s.Rule.Length > 0);
                if (subRules && !direct)
                {
                    view.tag.Text = "직접";
                    view.tag.Foreground = _ui["AccentText"];
                    view.reset.Visibility = Visibility.Visible;
                }
                _ui.Tip(view.reset, subRules ? "자동으로 — 이 줄과 아래 줄에서 고른 종류를 모두 프로그램 규칙으로 돌립니다." : "자동으로 — 프로그램 규칙의 종류로 돌립니다.");
                foreach (var sub in row.Subs)
                {
                    if (!_fwSubViews.TryGetValue((row.Type, row.Group, sub.Key), out var sv)) continue;
                    sv.row.Background = _fwPicked == (row.Type, row.Group) && _fwPickedSub == sub.Key ? _ui["AccentSoft"] : Brushes.Transparent;
                    sv.area.Text = M2(sub.Area);
                    if (sv.combo.Items.Count > 0 && sv.combo.Items[0] is ComboBoxItem { Tag: "" } auto && !Equals(auto.Content, SubAuto(row, sub))) auto.Content = SubAuto(row, sub);
                    SelectKind(sv.combo, sub.Rule);
                    string subKind = sub.Rule.Length > 0 ? sub.Rule : row.Rule.Length > 0 ? row.Rule : sub.Draft;
                    (byte r, byte g, byte b)? sc = subKind switch
                    {
                        FormworkSummary.Ask => FormworkPalette.Check,
                        FormworkSummary.Dropped => FormworkPalette.Dropped,
                        FormworkSummary.Mixed or FormworkSummary.PlateKinds => null,
                        _ => FormworkPalette.Base(subKind),
                    };
                    sv.swatch.Visibility = sc == null ? Visibility.Hidden : Visibility.Visible;
                    if (sc is { } srgb) sv.swatch.Background = new SolidColorBrush(Color.FromRgb(srgb.r, srgb.g, srgb.b));
                    bool subDirect = sub.Rule.Length > 0;
                    sv.tag.Text = subDirect ? "직접" : row.Rule.Length > 0 ? "줄" : "자동";
                    sv.tag.Foreground = subDirect ? _ui["AccentText"] : _sub;
                    sv.reset.Visibility = subDirect ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        finally { _syncing = false; }

        int checks = _board.CheckCount, gaps = _board.UncoveredChecks, splits = _board.KindSplits;
        _fwTotal.Inlines.Clear();
        _fwTotal.Inlines.Add(new Run("거푸집  ") { Foreground = _sub });
        _fwTotal.Inlines.Add(new Run(M2(_board.Total) + " ㎡") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        _fwTotal.Inlines.Add(new Run(checks > 0 ? $"      확인할 자리 {checks}곳" : "      확인할 자리 없음") { Foreground = checks > 0 ? _warn : _sub, FontSize = 12.5 });
        // 경고 딱지: 대응표 줄이 없어 3D 에 칠하지 않는 틈(아래 콘크리트와 너무 가까운 밑면 — 세지 않음 · core R42 M-3) ·
        // 엑셀에서 한 줄로 잇지 못한 자리(같은 평면에 맞닿았는데 대응표의 종류가 다르다 — JACK 2026-10-15: 잇지 않고 알린다). 종류를 바꿀 때마다 다시 센다.
        _fwWarnings.Children.Clear();
        foreach (var issue in _issues) _fwWarnings.Children.Add(IssueChip(issue, new Thickness(0, 4, 6, 0)));   // 모델의 문제(R63b) — 누르면 그 부재
        if (gaps > 0)
            _fwWarnings.Children.Add(WarnChip($"그림에 없는 틈 {gaps}곳",
                "틈 = 아래 콘크리트와 너무 가까워 거푸집을 댈 수 없는 밑면입니다(세지 않음 · 대응표 줄이 없어 3D 에 칠하지 않습니다).", "fw.warn.gaps"));
        if (splits > 0)
            _fwWarnings.Children.Add(WarnChip($"종류가 달라 못 이은 면 {splits}곳",
                "벽과 슬래브 옆처럼 같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 자리입니다. 종류를 맞추면 한 줄이 됩니다.\n"
                + string.Join("\n", _board.Merge.SplitSummary(5).Select(s => "  " + s)), "fw.warn.splits"));   // 풍선 글은 굴리지 않는다 — 앞 다섯 가지만(전부는 일지)
        // 사람이 지운 판(R48 — 1021 «지우기»): 합계 · 엑셀 · 거푸집 객체에서 빠진 것을 잊지 않게 · 옮긴 판(R55b) · 옮길 줄이 없어진 판 · 모양이 바뀌어 다시 센 판.
        int dropped = _board.DroppedCount, lost = _board.LostDrops.Count + _board.KindCheck.Lost.Count, moved = _board.MovedCount, orphans = _board.OrphanCount;
        int plateKinds = _board.PlateKindCount;
        if (dropped > 0)
            _fwWarnings.Children.Add(WarnChip($"지운 판 {dropped}장 · {M2(_board.DroppedArea)} ㎡",
                "3D 에서 지운 판입니다 — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다(부재 속성(지침용) 합계에는 들어 있습니다). 회갈색 판을 찍고 [되살리기] · [옮기기].", "fw.warn.dropped"));
        if (moved > 0)
            _fwWarnings.Children.Add(WarnChip($"옮긴 판 {moved}장 · {M2(_board.MovedArea)} ㎡",
                "다른 줄로 옮긴 판입니다 — 그 줄 · 그 종류로 셉니다(엑셀 · 거푸집 객체도). 부재 속성(지침용)은 옮기기 전 종류 그대로. 판을 찍고 [원래 줄로] · [지우기].\n"
                + string.Join("\n", _board.Rules.Moves.Take(5).Select(m => "  → " + RowLabel(m.to))) + (moved > 5 ? "\n  …" : ""), "fw.warn.moved"));
        if (plateKinds > 0)
            _fwWarnings.Children.Add(WarnChip($"Revit 판 종류 {plateKinds}장 · {M2(_board.PlateKindArea)} ㎡",
                "Revit 에서 판의 종류 칸을 고친 판입니다 — 그 판 한 장만 그 종류로 셉니다(표의 줄 종류와 상관없음 · 엑셀 · 부재 속성도). 판을 찍고 [원래 종류로].", "fw.warn.kinds"));
        if (orphans > 0)
            _fwWarnings.Children.Add(WarnChip($"옮길 줄이 없어진 판 {orphans}장",
                "옮긴 줄이 표에서 없어져(① 에서 부위 이름을 바꿈 · 줄이 가려짐) 지운 판으로 돌아갔습니다. 회갈색 판을 찍고 다시 [옮기기].", "fw.warn.orphan"));
        if (lost > 0)
        {
            // 기록을 지울 단추를 곁에 둔다(revit-verify R48 설치 전 M1 — 다시 찍어 빼도 옛 기록이 남아 경고가 영영 뜬다).
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(WarnChip($"모양이 바뀌어 다시 센 판 {lost}장",
                "전에 지우거나 옮긴 판의 자리를 이번 모양에서 찾지 못해 그 자리를 다시 셉니다(부재를 지웠거나 · 나중에 침 · 결합으로 면이 크게 바뀐 때). 필요하면 다시 찍어 지우세요.", "fw.warn.lost"));
            var forget = _ui.Button("기록 지우기", "BtnGhost", "찾지 못한 판의 기록을 대응표에서 지웁니다(그 자리는 이미 다시 세고 있어 합계는 그대로입니다).", "btn.fw.forgetLost");
            forget.Focusable = false;
            forget.Margin = new Thickness(0, 4, 6, 0);
            forget.VerticalAlignment = VerticalAlignment.Center;
            forget.Click += (_, _) => ForgetLostDrops();
            line.Children.Add(forget);
            _fwWarnings.Children.Add(line);
        }
        // 찾았지만 모양이 바뀐 뺀 판(core-verify R48 설치 전 M3): 일부 조각만 찾음 · 빠진 넓이가 뺄 때와 5% 넘게 다름.
        var check = _board.DropCheck;
        int kindPartial = _board.KindCheck.Partial.Count;
        var changedLeads = check.Partial.Concat(check.Resized).Distinct().ToList();
        if (changedLeads.Count > 0)
            _fwWarnings.Children.Add(WarnButton($"⚠ 모양이 바뀐 판 {check.Changed + kindPartial}",
                "전에 지운 판인데 모델이 바뀌어 넓이가 달라졌습니다. 누르면 그 판(여럿이면 차례로) — 다르면 [되살리기] 뒤 다시 지우세요.",
                "fw.warn.changed", () => PickDropped(changedLeads[_changedCycle++ % changedLeads.Count]), new Thickness(0, 4, 6, 0)));
        else if (kindPartial > 0)
            _fwWarnings.Children.Add(WarnChip($"모양이 바뀐 판 {kindPartial}장",
                (check.Partial.Count > 0 ? $"일부 조각만 찾은 판 {check.Partial.Count}장 — 찾은 조각만 빠지고 나머지는 다시 셉니다.\n" : "") +
                (kindPartial > 0 ? $"일부 조각만 찾은 Revit 판 종류 {kindPartial}장 — 찾은 조각만 그 종류이고 나머지는 줄의 종류입니다.\n" : "") +
                (check.Resized.Count > 0 ? $"빠진 넓이가 뺄 때와 다른 판 {check.Resized.Count}장 — 이웃 부재가 바뀌어 판이 커지거나 줄었습니다.\n" : "") +
                "판을 찍어 넓이를 보고, 다르면 [되살리기] 뒤 다시 지우세요.", "fw.warn.changed"));
        // 부위 줄에 가려진 옛 유형 줄(core-verify R48 설치 전 M1): 부위를 넣은 부재의 면은 부위 줄만 정한다 — 전에 고친 유형 줄이 표에 없고 걸리지도 않는다.
        var hidden = _board.HiddenTypeRules;
        if (hidden.Count > 0)
            _fwWarnings.Children.Add(WarnChip($"가려진 줄 {hidden.Count}",
                "고친 줄인데 그 면이 다른 줄에서 정해집니다 — 부위를 넣은 부재의 면은 부위 줄, 벽과 한 평면으로 이어진 면(한 덩어리)은 벽 줄. 이 줄은 걸리지 않습니다(대응표에는 남음).\n"
                + string.Join("\n", hidden.Take(5).Select(h => $"  {h.type} · {h.group} → {h.item}")) + (hidden.Count > 5 ? "\n  …" : ""), "fw.warn.hidden"));
        _fwWarnings.Visibility = _fwWarnings.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _fwKinds.Children.Clear();
        var parts = new List<string>();
        foreach (var t in _board.Totals)
        {
            string name = t.Band.Length > 0 ? $"{t.Item} {t.Band}" : t.Item;
            int band = t.Item == FormworkTable.Euro && t.Band != FormworkTable.BandName(0, _board.Options.Bands) ? 1 : 0;
            var c = FormworkPalette.Of(new FacePatch { Item = t.Item, Band = band });
            var text = new TextBlock { FontSize = 12 };
            text.Inlines.Add(new Run(name + "  "));
            text.Inlines.Add(new Run(M2(t.Area)) { FontWeight = FontWeights.SemiBold });
            var pill = _ui.Pill(text);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
            line.Children.Add(Swatch(c.r, c.g, c.b));
            line.Children.Add(pill);
            _fwKinds.Children.Add(line);
            parts.Add($"{name} {M2(t.Area)}");
        }
        _fwTotalText = $"거푸집 {M2(_board.Total)} ㎡ · 확인할 자리 {checks}곳" + (gaps > 0 ? $" · 그림에 없는 틈 {gaps}곳" : "") + (splits > 0 ? $" · 종류가 달라 못 이은 면 {splits}곳" : "") +
                       (dropped > 0 ? $" · 지운 판 {dropped}장" : "") + (moved > 0 ? $" · 옮긴 판 {moved}장" : "") + (orphans > 0 ? $" · 옮길 줄이 없어진 판 {orphans}장" : "") +
                       (lost > 0 ? $" · 모양이 바뀌어 다시 센 판 {lost}장" : "") + "\n" + string.Join(" · ", parts);
        _status.Text = _fwMoveLead != null ? "옮길 줄을 표에서 누르세요 · Esc = 취소"
            : (_board.Rules.Count > 0 ? $"대응표: 직접 고른 줄 {_board.Rules.Count}" : "대응표: 모두 자동") + (dropped > 0 ? $" · 지운 판 {dropped}장" : "") + (moved > 0 ? $" · 옮긴 판 {moved}장" : "") + " · 고칠 때마다 모델에 적힙니다";
        // [되돌리기](② 에서 한 일 — R55b): 할 것이 없으면 꺼짐 · 풍선 글 = 되돌릴 일.
        _undo.IsEnabled = _fwUndo.Count > 0;
        _ui.Tip(_undo, _fwUndo.Count > 0 ? $"되돌리기(Ctrl+Z): {_fwUndo[^1].what}" : "되돌릴 것이 없습니다.");

        // 찍은 판의 띠: 엑셀 줄 한 줄 + 세는 판 [지우기] · 지운 판 [옮기기] [되살리기] · 옮긴 판 [원래 줄로] [지우기](R55b)
        // (판이 아닌 덩이 — 세지 않음 · 아직 정하지 않은 밑면 — 는 단추 없이 글만).
        if (_fwPlateBar != null && _fwPlateButton != null && _fwMoveButton != null)
        {
            var piece = _fwPickedIndex >= 0 ? PieceAt(_fwPickedIndex) : null;
            _fwPlateBar.Visibility = piece == null ? Visibility.Collapsed : Visibility.Visible;
            if (piece != null)
            {
                _fwPlateText.Text = PieceText(piece);
                _fwPlateText.ToolTip = _fwPlateText.Text;
                _fwPlateButton.Content = piece.Dropped ? "되살리기" : "지우기";
                _fwPlateButton.Visibility = piece.Dropped || piece.Moved || piece.Plate != null ? Visibility.Visible : Visibility.Collapsed;
                _fwMoveButton.Content = piece.Moved ? "원래 줄로" : _fwMoveLead != null ? "줄 고르는 중…" : "옮기기";
                _fwMoveButton.Visibility = piece.Dropped || piece.Moved ? Visibility.Visible : Visibility.Collapsed;
                _fwMoveButton.Style = _ui.StyleOf(_fwMoveLead != null && piece.Dropped ? "BtnPrimary" : "BtnSoft");
                if (_fwKindClearButton != null) _fwKindClearButton.Visibility = !piece.Dropped && piece.KindLead.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            // 둘째 줄: 그 판의 줄 종류(지금 걸린 종류 = 진한 단추) · 직접 고른 줄이면 ↺ · 범위. 뺀 판 · 줄이 없는 덩이는 숨긴다.
            var key = piece != null && !piece.Dropped ? _board.RowOf(piece) : null;
            var row = key is { } k ? RowOf(k) : null;
            _fwKindBar.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
            if (row != null)
            {
                // 아래 줄을 보이는 줄이면 그 판의 아래 줄(R64): 지금 걸린 종류 = 아래 줄 값 > 줄 값 > 그 아래 줄의 자동.
                var subRow = PickedPlateSub((row.Type, row.Group)) is { } subKey ? row.Subs.FirstOrDefault(s => s.Key == subKey) : null;
                string now = subRow is { Rule.Length: > 0 } ? subRow.Rule : row.Rule.Length > 0 ? row.Rule : subRow?.Draft ?? row.Draft;
                bool banded = row.Group is not (FormworkTable.GroupSlope or FormworkTable.GroupHole);
                foreach (var (item, button) in _fwKindButtons)
                {
                    button.Visibility = banded || item != FormworkTable.Euro ? Visibility.Visible : Visibility.Collapsed;
                    button.Style = _ui.StyleOf(item == now ? "BtnPrimary" : "BtnSecondary");
                    _ui.Tip(button, $"{ScopeText(row)} → {item}");
                }
                bool direct = subRow != null ? subRow.Rule.Length > 0 : row.Rule.Length > 0;
                if (_fwKindAuto != null) _fwKindAuto.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
                _fwKindScope.Text = (direct ? "직접" : subRow != null && row.Rule.Length > 0 ? "줄" : "자동") + " · 함께 바뀜: " + ScopeText(row) + (subRow != null ? " · " + SubLabel(row, subRow) : "");
            }
        }
    }

    // 표 맨 아래 «지운 판»(R56a): 머리 «지운 판 n장 · ㎡» + 판마다 한 줄 «{부위 · 유형} · {면}(안팎) · 넓이» — 누르면 3D 에서 그 판을 찍고 화면을 맞춘다
    // ([옮기기] · [되살리기]는 띠에). 지운 판이 바뀔 때만 다시 짓는다 · 찍은 판의 줄은 바탕을 칠한다.
    private void RefreshDroppedRows()
    {
        if (_board == null) return;
        var dropped = _board.Pieces.Where(p => p.Dropped).ToList();
        string key = string.Join("|", dropped.Select(p => p.DropLead + ":" + M2(p.Area)));
        if (key != _fwDropKey)
        {
            _fwDropKey = key;
            _fwDropRows.Children.Clear();
            _fwDropLines.Clear();
            if (dropped.Count > 0)
            {
                var title = new TextBlock { Margin = new Thickness(2, 12, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                title.Inlines.Add(new Run("지운 판") { FontWeight = FontWeights.SemiBold });
                title.Inlines.Add(new Run($"  {dropped.Count}장 · {M2(dropped.Sum(p => p.Area))} ㎡") { Foreground = _sub, FontSize = 12 });
                _ui.Mark(title, "3D 에서 지운 판 — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다. 줄을 누르면 그 판을 찍습니다: [옮기기]로 다른 줄에 넣거나 [되살리기].", "fw.drop.head");
                _fwDropRows.Children.Add(title);
                foreach (var piece in dropped.OrderBy(DropLabel, StringComparer.Ordinal))
                {
                    string lead = piece.DropLead;
                    var grid = RowGrid();
                    var name = new TextBlock { Text = DropLabel(piece), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    _ui.Tip(name, PieceText(piece));
                    var area = new TextBlock { Text = M2(piece.Area), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                    var kind = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
                    kind.Children.Add(Swatch(FormworkPalette.Dropped.r, FormworkPalette.Dropped.g, FormworkPalette.Dropped.b));
                    kind.Children.Add(new TextBlock { Text = "지운 판", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    Grid.SetColumn(area, 1);
                    Grid.SetColumn(kind, 2);
                    Grid.SetColumnSpan(kind, 2);
                    grid.Children.Add(name);
                    grid.Children.Add(area);
                    grid.Children.Add(kind);
                    var line = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 4, 5), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                    _ui.Mark(line, "", "fw.drop." + _fwDropLines.Count);
                    line.MouseLeftButtonUp += (_, _) => PickDropped(lead);
                    _fwDropRows.Children.Add(line);
                    _fwDropLines[lead] = line;
                }
            }
        }
        string picked = PickedPlatePiece() is { Dropped: true } pp ? pp.DropLead : "";
        foreach (var (lead, line) in _fwDropLines) line.Background = lead == picked ? _ui["AccentSoft"] : Brushes.Transparent;
    }

    // 지운 판 한 줄의 이름: «{부위} · {유형} · {면 갈래}(안팎) · Id» — 같은 글의 줄을 Id 로 가른다(계획 검토 L2).
    private static string DropLabel(FormworkPiece piece)
    {
        var lead = piece.LeadMember;
        var el = lead.Result.Element;
        string owner = el.Part.Length > 0 ? el.Part : el.Type;
        string side = lead.Patch.Side.Length > 0 ? $"({lead.Patch.Side})" : "";
        return $"{owner} · {el.Type} · {lead.Group}{side} · Id {el.Id}";
    }

    // 표의 «지운 판» 줄을 눌렀다: 3D 에서 그 판을 찍고 그 판이 보이게 화면을 맞춘다(고른 줄은 그대로 — 그 줄의 판도 보인다).
    private void PickDropped(string lead)
    {
        if (_board == null || Busy) return;
        int at = _board.IndexOfDrop(lead);
        if (at < 0) return;
        var piece = _board.Pieces[at];
        _fwPlate = piece.Lead;
        _fwMoveLead = null;
        _rvMembers.Clear();
        RefreshAll();
        // 보기 거르기(종류 · 층 · 숨긴 부재)로 빠진 판이면 화면은 그대로 둔다 — 안 보이는 판으로 맞추지 않는다(설치 전 검토 L2 · PickRow 와 같이).
        if (!piece.Mesh.Box.IsEmpty && FormworkShown(MemberCount + at)) _view3d.ZoomTo(piece.Mesh.Box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
        FollowPiece(piece);   // R61a
        if (_fwDropLines.TryGetValue(lead, out var line)) line.BringIntoView();
    }

    // 종류 단추가 바꾸는 범위(대응표의 그 줄): 부위 줄 = 그 부위 전체 · 유형 줄 = 그 유형의 그 면.
    private static string ScopeText(FormworkRow row) =>
        FormworkRules.PartOf(row.Type) is { } part ? $"부위 {part} 전체" : $"{row.Type} 의 {row.Group}";

    // 찍은 판이 든 대응표 줄(뺀 판 · 줄이 없는 덩이면 null).
    private (string type, string group)? PickedPlateRow() =>
        _board != null && _fwPickedIndex >= 0 && PieceAt(_fwPickedIndex) is { Dropped: false } piece ? _board.RowOf(piece) : null;

    private static string M2(double v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // 경고 딱지 하나: 짧은 이름표 + 풀이는 풍선 글(창 안 설명은 최소로 — JACK 1014).
    private FrameworkElement WarnChip(string text, string tip, string id)
    {
        var chip = new Border
        {
            Background = _ui["WarnBg"], CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 4, 6, 0),
            Child = new TextBlock { Text = text, Foreground = _warn, FontSize = 12 },
        };
        _ui.Mark(chip, tip, id);
        return chip;
    }

    // ── 시험에서 쓰는 길 ─────────────────────────────────────────────────────────

    /// <summary>그 줄의 종류를 골랐다(화면에서 고르는 칸을 바꾼 것과 같은 길 — 기다리지 않고 바로).</summary>
    public bool ChooseKind(string type, string group, string choice) => ChangeRule((type, group), choice);
    /// <summary>3D 에서 그 덩이를 찍었다(덩이의 차례).</summary>
    public void ClickPiece(int piece) => ClickFormwork(MemberCount + piece);
    /// <summary>표에서 그 줄을 눌렀다(줄 칸을 누른 것과 같은 길).</summary>
    public void PickTableRow(string type, string group) => PickRow((type, group));
    /// <summary>지금 고른 줄.</summary>
    public (string type, string group)? PickedRow => _fwPicked;
    /// <summary>시험용(R64): 고른 아래 줄(null = 줄 전부) · 아래 줄의 종류를 고름(화면의 칸과 같은 길) · 아래 줄을 누름.</summary>
    public string? PickedSub => _fwPickedSub;
    public bool ChooseSubKind(string type, string group, string sub, string choice) => ChangeSubRule((type, group), sub, choice);
    public void PickTableSub(string type, string group, string sub) => PickRow((type, group), sub);
    /// <summary>그림 위 띠의 글(합계 · 종류별).</summary>
    public string FormworkTotalText => _fwTotalText;
    /// <summary>그 덩이의 3D 색(시험에서 본다).</summary>
    public (byte r, byte g, byte b) PieceColor(int piece) => FormworkColor(MemberCount + piece);
    /// <summary>그 덩이가 흰 막으로 덮여 있는가(찍은 판).</summary>
    public bool PieceHighlighted(int piece) => FormworkPicked(MemberCount + piece);
    /// <summary>그 덩이 · 부재를 그리는 방식(고른 줄이면 그 줄의 판 = 맨 위, 나머지 = 옅게).</summary>
    public MeshStyle PieceStyle(int piece) => FormworkStyle(MemberCount + piece);
    public MeshStyle MemberStyle(int member) => FormworkStyle(member);
    /// <summary>찍은 판의 덩이 차례(없으면 −1).</summary>
    public int PickedPiece => _fwPickedIndex >= 0 ? _fwPickedIndex - MemberCount : -1;
    /// <summary>찍은 판의 띠 글 · 단추 글(띠가 없으면 "").</summary>
    public string PlateBarText => _fwPlateBar is { Visibility: Visibility.Visible } ? _fwPlateText.Text : "";
    public string PlateButtonText => _fwPlateBar is { Visibility: Visibility.Visible } && _fwPlateButton is { Visibility: Visibility.Visible } b ? b.Content as string ?? "" : "";
    /// <summary>찍은 판의 띠 단추([지우기] · [되살리기])를 눌렀다(화면의 단추와 같은 길).</summary>
    public bool PressPlateButton() => PlateAction();
    /// <summary>표 맨 아래 «지운 판» 줄의 수(R56a).</summary>
    public int DroppedLineCount => _fwDropLines.Count;
    /// <summary>표의 «지운 판» 줄 하나(차례 — 화면의 차례 · 없으면 null).</summary>
    public Border? DroppedLine(int index) => index >= 0 && index < _fwDropLines.Count ? _fwDropLines.Values.ElementAt(index) : null;
    /// <summary>찍은 판의 [옮기기] · [원래 줄로] 단추(보이지 않으면 null).</summary>
    public Button? MoveButton => _fwPlateBar is { Visibility: Visibility.Visible } && _fwMoveButton is { Visibility: Visibility.Visible } b ? b : null;
    /// <summary>옮길 줄을 고르는 중인가.</summary>
    public bool MovePicking => _fwMoveLead != null;
    /// <summary>② 에서 되돌릴 수 있는 일의 수.</summary>
    public int FormworkUndoCount => _fwUndo.Count;
    /// <summary>[되돌리기] 단추(① · ② 같이 씀).</summary>
    public Button UndoButton => _undo;
    /// <summary>찍은 판의 띠 단추(진짜 마우스로 누를 때 자리를 잰다).</summary>
    public Button? PlateButton => _fwPlateButton;
    public Button? KindClearButton => _fwKindClearButton;
    /// <summary>찍은 판의 줄 종류 단추(종류 이름 · «자동»은 ↺) — 보이지 않으면 null.</summary>
    public Button? PlateKindButton(string item) =>
        _fwKindBar.Visibility != Visibility.Visible ? null : item.Length == 0 ? (_fwKindAuto is { Visibility: Visibility.Visible } a ? a : null)
        : _fwKindButtons.TryGetValue(item, out var b) && b.Visibility == Visibility.Visible ? b : null;
}
