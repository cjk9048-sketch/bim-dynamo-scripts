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
///  · 왼쪽 판(R66 — JACK 2026-10-27 «판을 먼저 만들고 그다음 지정»): [자동 | 사용자] · 위치 나무(R73a — 층 → 중분류 → 부재 → 면 · 거름 [전체 | 확인할 자리 | 안 셈] ·
///    전의 판 목록 · 지운 판 묶음 대신 — PlacePartView.Tree) · 사용자 모드의 «유형 · 부위별로 한꺼번에»(옛 대응표 — 줄 규칙). 기록은 바꿀 때마다 모델에 적는다(SaveRules).
///  · 그림 위 = 거푸집 합계 · 종류별 넓이 · 경고 · 고른 판의 띠(종류 · 세지 않음 · 자동으로 · 부재마다 나누기 · 합치기 · 자리 바꾸기 — PlacePartView.Plates), 그림 아래 = 색 풀이.
///  · 3D 에서 판을 찍으면(Ctrl = 더하기 · 끌기 = 네모로) 흰 막 + 분홍 테두리로 덮는다. 종류 · 층 거름은 ① 과 같이 쓴다(① 에서 숨긴 부재는 숨긴 채다).
/// </summary>
public sealed partial class PlacePartView
{
    private FormworkBoard? _board;
    private FrameworkElement? _fwSide, _fwTop, _fwBottom;
    private readonly StackPanel _fwRows = new();
    // 표 맨 아래 «지운 판» 묶음(R56a)은 R73a 에서 걷었다 — 위치 나무의 거름 [안 셈](PlacePartView.Tree)이 그 자리.
    private readonly TextBlock _fwTotal = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _fwKinds = new() { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
    // 경고 딱지(그림에 없는 틈 · 종류가 달라 못 이은 면): 합계 줄 끝에 이으면 좁은 창에서 «…»로 잘려 안 보인다 — 따로 줄바꿈되게 둔다(revit R43 R4).
    private readonly WrapPanel _fwWarnings = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<(string type, string group), (Border row, ComboBox combo, TextBlock area, Border swatch, TextBlock tag, Button reset)> _fwRowViews = new();
    // 아래 줄(R64 — JACK 2026-10-26 «여러 종류는 풀어서 종류별로» · 고름 «종류별 아래 줄로 풀기»): 줄 밑 «└» 줄 · 아래 줄을 보이는 줄(보드를 지을 때 정함 — 보드 동안 그대로 · critic R64 M3).
    private readonly Dictionary<(string type, string group, string sub), (Border row, ComboBox combo, TextBlock area, Border swatch, TextBlock tag, Button reset)> _fwSubViews = new();
    private readonly HashSet<(string type, string group)> _fwSubRows = new();
    private string? _fwPickedSub;      // 고른 줄의 아래 줄(null = 줄 전부)
    private (string type, string group)? _fwPicked;
    private string _fwTotalText = "";
    private IReadOnlyList<MeshData> _fwMemberMeshes = Array.Empty<MeshData>();   // ② 의 부재 겉면(덮인 모서리 선을 뺀 것 — R48b)
    private int _fwPickedIndex = -1;                                   // 띠의 첫 판(고른 판의 마지막)의 그림 차례(없으면 −1)
    private readonly HashSet<int> _fwPickedSet = new();                // 고른 판들의 덩이 차례(R66 — 흰 막)
    private MeshStyle[] _fwStyle = Array.Empty<MeshStyle>();           // 차례마다 그리는 방식(고른 것이 바뀔 때 한 번 정해 둔다)
    private readonly TextBlock _fwPlateText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
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
            _fwSel.Clear();
            _fwSelKeys.Clear();
            _fwFaces.Clear();                      // 고른 면 · 면 겉면 · 나무 · 지도도 새 판으로(R73a)
            _fwFaceKeys.Clear();
            _fwFaceMeshes.Clear();
            _fwHoverFace = null;
            _fwTreeFaces = null;
            _fwTree = null;
            _fwMarksFor = null;
            if (_fwIso != null) _fwIsoEndWhy = "판을 새로 지음";
            _fwIso = null;                         // 부재만 보기(R73a2)도 새 판으로
            _fwIsoClipCache.Clear();               // 잘라 그린 몫(R73a3)도
            _fwIsoClipKey = null;
            _fwMeasured = board.MeasureVersion;
            _fwPlaceOpen = false;
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
        // 줄 · 판을 고른 동안 옅게 그린 부재는 찍히지 않는다 — 그 뒤의 판을 찍어 바로 옮겨 고를 수 있게(부재를 찍어도 ② 에서는 할 일이 없다).
        // 옅게 그린 판은 찍힌다(R73a — 전에는 윤곽선만 · GhostPickable). 고른 면을 보이는 동안은 그 면의 부재(보통)도 찍지 않는다(그 위의 옅은 판을 찍게).
        _view3d.Pickable = i => i < OverlayStart && (i >= MemberCount || (!_fwFocusLook && FormworkStyle(i) == MeshStyle.Solid));   // 덧그린 몫 · 고른 면은 찍지 않는다(그 밑의 판을 찍는다)
        _view3d.GhostPickable = true;
        _view3d.PickFilm = i => i < FaceOverlayStart;      // 고른 면(R73a)은 흰 막 없이 제 색 + 분홍 테두리
        _view3d.OutlinesOnTop = true;                      // 분홍 테두리는 맨 위 층에(덧그림 층 한 겹을 덜 그린다 — 계획 §5)
        _view3d.SeenFirst = true;                          // 찍기 = 보이는 겉면 먼저(가려진 판을 잡지 않게 — 검토 높음 1)
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
        if (_board.MeasureVersion != _fwMeasured) { SyncMeasured(); selectionOnly = false; }   // 형상을 다시 잰 판(R67)
        bool hadOverlay = _fwOverlay.Count > 0 || _fwFaceOverlay.Count > 0 || _fwClipsDrawn.Count > 0;
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
        // 고른 면만의 겉면(R73a — ② 에서만 · 맨 뒤 차례 · 맨 위 층에 제 색 + 분홍 테두리). 부재 줄로 고른 «이 부재만 보기»(R73a4)는 면 하나를 밝히지 않는다.
        PruneFaces();
        _fwFaceOverlay.Clear();
        _fwFaceOverlayFor.Clear();
        _fwFaceOverlayFor.AddRange(_fwFaces);
        _fwFaceOverlayWhole = SoloWhole;
        if (Step == 1 && !_fwFaceOverlayWhole)
            foreach (var face in FocusFaces()) _fwFaceOverlay.Add((FaceMesh(face), face));
        if (selectionOnly && !hadOverlay && _fwOverlay.Count == 0 && _fwFaceOverlay.Count == 0)
        {
            UpdateFormworkLook();
            // 부재만 보기의 잘라 그린 몫(R73a3)이 생겼으면 · 이 부재만 보기(R73a4)가 끝나 판의 뺀 몫이 바뀌면 겉면을 갈아 끼운다.
            if (!ClipsChanged && HiddenKey() == _fwMaskKey) return;
        }
        var meshes = new List<MeshData>(_memberMeshes.Count + _board.Pieces.Count + _fwOverlay.Count + _fwFaceOverlay.Count + _fwIsoClips.Count);
        meshes.AddRange(_fwMemberMeshes.Count == _memberMeshes.Count ? _fwMemberMeshes : _memberMeshes);
        _fwMaskKey = HiddenKey();
        for (int i = 0; i < _board.Pieces.Count; i++) meshes.Add(PieceMesh(i));   // 숨긴 부재 몫을 뺀 판(R73a3 — 이은 판) · 이 부재만 보기의 다른 부재 몫도(R73a4)
        meshes.AddRange(_fwOverlay.Select(o => o.mesh));
        meshes.AddRange(_fwFaceOverlay.Select(o => o.mesh));
        UpdateFormworkLook();                      // 닿은 부재 · 잘라 그린 몫(R73a3)도 여기서 — 맨 뒤 차례로 붙인다
        // 고르기가 무리 밖으로 나가 이 부재만 보기가 방금 끝났다(R73a4 — 문제 딱지 · 검사 딱지 등): 뺀 몫 · 면 겉면을 모두 보기로 한 번 다시 짓는다.
        if (HiddenKey() != _fwMaskKey) { SetFormworkMeshes(); return; }
        meshes.AddRange(_fwIsoClips.Select(c => c.mesh));
        _fwClipsDrawn.Clear();
        _fwClipsDrawn.AddRange(_fwIsoClips);
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
        _view3d.PickFilm = _ => true;
        _view3d.OutlinesOnTop = false;
        _view3d.SeenFirst = false;
        _view3d.SetHoverOutline(null);
        _fwHoverFace = null;
        _view3d.SetMeshes(_memberMeshes);
        _view3d.SetGuides(Array.Empty<(IReadOnlyList<Vec3>, bool, int)>());   // ② 의 판 모양 검사 빨간 선이 ① 에 남지 않게(R68 설치 전 검토 중간)
        _view3d.SetLabels(Array.Empty<(Vec3, string)>());                       // ② 의 고른 면 이름 · 치수(R73a)도
        _fwMarksFor = null;
        _hoverPill.Margin = new Thickness(10);
        if (_fwIso != null) _fwIsoEndWhy = "① 로";
        _fwIso = null;                             // 부재만 보기(R73a2)는 ② 에서만
        _fwIsoClips.Clear();                       // 잘라 그린 몫(R73a3)도 — ① 의 겉면은 부재만
        _fwClipsDrawn.Clear();
        RefreshIsoBar();
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ── 3D ───────────────────────────────────────────────────────────────────────

    // 숨긴 부재(R73a3 — Del · ① 숨기기)를 뺀 판 겉면: 여러 부재를 이은 판은 숨긴 부재 몫만 빠진다(FormworkBoard.MeshWithout — 세모 차례 그대로) ·
    // 숨긴 것이 같은 동안 다시 쓴다. 다 숨긴 판은 거름(FormworkShown)이 숨긴다.
    private string? _fwMaskKey;
    private readonly Dictionary<MeshData, (string key, MeshData mesh)> _fwMasked = new(ReferenceEqualityComparer.Instance);

    // R73a4 «이 부재만 보기»도 같은 길: 고른 부재가 아닌 부재의 몫을 뺀다(열쇠에 고른 부재를 붙인다 — 바뀌면 겉면을 갈아 끼운다).
    private string HiddenKey() => (_editor.Hidden.Count == 0 ? "" : string.Join(",", _editor.Hidden.Order())) + (SoloOn ? "|solo " + string.Join(",", _fwIso!.Order()) : "");

    private MeshData PieceMesh(int piece)
    {
        var whole = _board!.Pieces[piece].Mesh;
        if (Step != 1) return whole;                                   // ③ 은 그림 그대로
        bool solo = SoloOn;
        if (_editor.Hidden.Count == 0 && !solo) { _fwMasked.Clear(); return whole; }
        var members = _board.Pieces[piece].Members;
        // 이 부재만 보기: 고른 부재가 안 든 판은 그리지 않으니(FormworkShown) 자르지 않는다 · 든 판은 다른 부재 몫을 뺀다.
        if (solo && !members.Any(m => _fwIso!.Contains(m.Result.Element.Id))) return whole;
        if (_fwMasked.Count > 2 * _board.Pieces.Count + 16) _fwMasked.Clear();   // 판을 다시 지어 묵은 겉면이 쌓이면 비운다
        string key = HiddenKey();
        if (_fwMasked.TryGetValue(whole, out var made) && made.key == key) return made.mesh;
        IReadOnlySet<long> off = _editor.Hidden;
        if (solo) off = members.Select(m => m.Result.Element.Id).Where(id => !_fwIso!.Contains(id)).Concat(_editor.Hidden).ToHashSet();
        var mesh = _board.MeshWithout(piece, off) ?? whole;
        _fwMasked[whole] = (key, mesh);
        return mesh;
    }

    // «이 부재만 보기»(R73a4)가 그 부재를 보이는가: 고른 부재 · 숨기지 않음(종류 · 층 거름은 넘는다 — 거른 부재를 나무에서 눌러 빈 그림이 되지 않게 · 내 판단).
    private bool SoloShows(long id) => _fwIso != null && _fwIso.Contains(id) && !_editor.Hidden.Contains(id);

    private FormworkPiece PieceAt(int index) => _board!.Pieces[index - MemberCount];

    // 그림 차례: 부재 → 덩이 → 덧그린 줄 몫(R57 P1b) → 고른 면(R73a). 덧그린 몫 · 고른 면은 그 밑 덩이의 거름 · 색을 따른다.
    private int OverlayStart => MemberCount + (_board?.Pieces.Count ?? 0);
    private int OverlayPiece(int index) =>
        index >= OverlayStart && index - OverlayStart < _fwOverlay.Count ? MemberCount + _fwOverlay[index - OverlayStart].piece
        : index >= FaceOverlayStart && index - FaceOverlayStart < _fwFaceOverlay.Count && _fwFaceOverlay[index - FaceOverlayStart].face.Piece >= 0 ? MemberCount + _fwFaceOverlay[index - FaceOverlayStart].face.Piece
        : -1;

    // 부재와 그 부재의 덩이는 같은 거름(종류 · 층 · 숨긴 부재)을 탄다. '남은 것만 보기'는 보지 않는다(다 넣은 부재에도 거푸집이 있다).
    // 부재를 넘어 이은 판은 든 부재 하나라도 보이면 보인다. 판에 안 든 고른 면(그림에 없는 틈 — R73a)은 그 부재를 따른다.
    // R73a4 «이 부재만 보기»: 고른 부재 · 그 부재가 든 판만(판은 그 부재 몫만 — PieceMesh) · 나머지는 아예 그리지 않는다(찍기 · 화면 맞춤도 빠진다).
    private bool FormworkShown(int index)
    {
        bool solo = SoloOn;
        if (index < MemberCount) return solo ? SoloShows(_members[index].Id) : _editor.InView(_members[index]);
        if (ClipSource(index) is int source and >= 0) return FormworkShown(source);   // 잘라 그린 몫(R73a3)은 그 부재 · 판을 따른다(숨기면 함께)
        if (index >= FaceOverlayStart && index - FaceOverlayStart < _fwFaceOverlay.Count) return FaceShown(_fwFaceOverlay[index - FaceOverlayStart].face);
        if (index >= OverlayStart) return OverlayPiece(index) is int under && under >= 0 && FormworkShown(under);
        foreach (var m in PieceAt(index).Members)
            if (solo ? SoloShows(m.Result.Element.Id) : (_editor.Find(m.Result.Element.Id) is not { } member || _editor.InView(member))) return true;
        return false;
    }

    // 부재 = 옅은 회색. 판 = 종류의 색(확인할 자리는 분홍) · 세지 않기로 한 덩이는 부재와 같은 회색 · 사람이 뺀 판은 옅은 회갈색(R48).
    private (byte r, byte g, byte b) FormworkColor(int index)
    {
        var grey = (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
        if (index < MemberCount) return _rvMembers.Contains(_members[index].Id) ? ConcreteColor : _fwIso != null && _fwIsoNear.Contains(_members[index].Id) ? NearTint : grey;   // 문제 딱지로 고른 부재(R63b — ② 도 ③ 처럼 청록) · 부재만 보기의 닿은 부재(R73a2)
        if (ClipSource(index) is int source and >= 0) return FormworkColor(source);    // 잘라 그린 몫(R73a3) = 청록 몸 · 씻은 판
        if (index >= FaceOverlayStart) return index - FaceOverlayStart < _fwFaceOverlay.Count ? FaceColor(_fwFaceOverlay[index - FaceOverlayStart].face) : grey;   // 고른 면 = 제 색(R73a)
        if (index >= OverlayStart)
        {
            // 덧그린 몫은 그 줄의 색(높이 구간이 다른 줄이면 밑 판의 대표와 색이 다르다 — P1b 검토 L1).
            if (index - OverlayStart >= _fwOverlay.Count) return grey;
            var part = _fwOverlay[index - OverlayStart].lead;
            return part.Counted || part.NeedsCheck ? FormworkPalette.Of(part) : grey;
        }
        var piece = PieceAt(index);
        var lead = piece.Lead;
        var color = piece.Dropped ? FormworkPalette.Dropped : lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : grey;
        // 부재만 보기(R73a2 — JACK «선택한 부재와 닿은 부재는 서로 색을 달리»): 닿은 부재만 든 판은 씻은 색(맥락) · 고른 부재의 판은 제 색 그대로.
        return _fwIso != null && _fwIsoNearPieces.Contains(index - MemberCount) ? Washed(color) : color;
    }

    // 흰 막 + 분홍 테두리: 고른 판(R48 · R66 — 여러 장) · [완료]의 산출식 확인 창이 보여 주는 부재. 고른 줄은 막 대신 그 줄의 판을 맨 위에 진하게 그린다(FormworkStyle).
    // 고른 면(R73a)은 분홍 테두리만(PickFilm) — 그때 판은 막 없이 옅게(판 전체의 가는 둘째 테두리는 R73a2 에서 걷음 — JACK 1030 «허공에 뜬 틀»).
    // R73a4: 부재 줄로 고른 «이 부재만 보기»는 막도 테두리도 없다(그 부재의 판이 다 고른 판이라 — 거푸집 색이 막에 가려지지 않게).
    private bool FormworkPicked(int index) =>
        index < MemberCount ? _fwFocus is long focus && _members[index].Id == focus
        : index >= FaceOverlayStart ? index - FaceOverlayStart < _fwFaceOverlay.Count
        : index < OverlayStart && !_fwFocusLook && _fwPickedSet.Contains(index - MemberCount) && !SoloWhole;

    // 그리는 방식(R48 — JACK 2026-10-16 «고른 줄만 진하게, 나머지 흐리게» · 2026-10-21 «목록을 누르면 그 자리만 색 · 나머지는 색 없는 투명에 윤곽선만»):
    // 고른 것이 없으면 모두 보통(면 + 모서리). 줄이나 판을 고르면 그 줄의 판(줄에 든 조각이 하나라도 있는 판 — 이은 판은 통째로)과 찍은 판은 맨 위에 제 색 ·
    // 부재와 다른 거푸집은 면 없이 윤곽선만(Ghost).
    private MeshStyle FormworkStyle(int index) => index < _fwStyle.Length ? _fwStyle[index] : MeshStyle.Solid;

    // 고른 줄 · 찍은 판으로 그림 차례마다의 모습을 정해 둔다(그리기 · 찍기가 차례마다 묻는다 — 마우스가 움직일 때마다 줄을 다시 세지 않게).
    // 찍은 판은 대표 조각으로 다시 찾는다(덩이를 다시 지어 차례가 바뀌어도) — 없어졌으면 푼다.
    private void UpdateFormworkLook()
    {
        if (_board != null && _board.MeasureVersion != _fwMeasured) SyncMeasured();   // 형상을 다시 잰 판(R67) — 고른 판을 먼저 다시 잡는다
        // 고른 판(R66): 조각으로 다시 찾는다 — 없어진 것 · 같은 판을 가리키게 된 것(합침)은 뺀다.
        _fwPickedSet.Clear();
        var kept = new List<FacePatch>();
        if (_board != null)
            foreach (var anchor in _fwSel)
                if (_board.IndexOf(anchor) is int at && at >= 0 && _fwPickedSet.Add(at)) kept.Add(anchor);
        _fwSel.Clear();
        _fwSel.AddRange(kept);
        PruneFaces();
        KeepFaceKeys();
        _fwPickedIndex = _board != null && PrimaryPlate is { } primary ? MemberCount + _board.IndexOf(primary) : -1;
        KeepSelectionKeys();   // 형상을 다시 재면(R67) 이 열쇠로 고른 판을 다시 잡는다
        IsolateSets();         // 부재만 보기(R73a2) — 무리 밖을 고르면 모두 보기로
        BuildIsoClips();       // 닿은 부재 · 판의 잘라 그린 몫(R73a3 — 맨 뒤 차례)
        RefreshIsoBar();
        int count = ClipStart + _fwIsoClips.Count;
        var styles = new MeshStyle[count];
        // 나머지의 모습: ② 는 옅게(R73a — 반투명 음영 · 철사 선 아님) · ③ 은 윤곽선 그대로.
        var rest = Step == 1 ? MeshStyle.Faded : MeshStyle.Ghost;
        _fwFocusLook = false;
        if (_rvMembers.Count > 0)
        {
            // ③ 콘크리트 줄 · 미산출 부재(R59) · 문제 딱지(R63b — ② 도): 그 부재만 진하게 · 나머지 부재는 옅게(②) · 윤곽선(③) · 판은 숨김(③ ReviewShown) · 옅게(②).
            for (int i = 0; i < MemberCount; i++) styles[i] = _rvMembers.Contains(_members[i].Id) ? MeshStyle.Top : rest;
            for (int i = MemberCount; i < count; i++) styles[i] = rest;
        }
        else if (_board != null && Step == 1 && _fwIso != null)
        {
            // 부재만 보기(R73a2): 고른 부재와 그 판 = 보통(바닥 층 — 연회색 몸 + 제 색 판) · 고른 면 = 맨 위(제 색 + 분홍 테두리) ·
            // 나머지 부재 · 판 = 흐린 윤곽(Faint — 찍히지도 가리지도 않음 · 판에 덮여 지운 부재 모서리는 판 테두리가 대신 그린다).
            // R73a3: 닿은 부재 · 닿은 부재만의 판도 흐린 윤곽 — 고른 부재 상자 + 0.3 m 안의 잘라 그린 몫만 보통(청록 몸 · 씻은 판 · 찍히지 않음 — Pickable 은 덩이 차례까지).
            _fwFocusLook = _fwFaceOverlay.Count > 0;
            for (int i = 0; i < count; i++)
                styles[i] = i >= ClipStart ? MeshStyle.Solid : i >= FaceOverlayStart ? MeshStyle.Top : i < OverlayStart && InIsolate(i) ? MeshStyle.Solid : MeshStyle.Faint;
        }
        else if (_board != null && Step == 1 && _fwFaceOverlay.Count > 0)
        {
            // 고른 면(R73a — 3D 찍기): 그 면 = 맨 위(제 색 + 분홍 테두리) · 그 부재 = 연회색 그대로 · 나머지(그 면의 판도) = 옅게.
            // 그 부재도 맨 위 층에 둔다 — 옅게는 바닥 층에 불투명하게 그리므로(계획 §5 대신 꼴) 앞의 옅은 것에 가려지지 않게(내 판단 — 계획은 «Solid»).
            _fwFocusLook = true;
            var owners = _fwFaceOverlay.Select(o => IndexOf(o.face.ElementId)).Where(i => i >= 0).ToHashSet();
            for (int i = 0; i < count; i++)
                styles[i] = i >= FaceOverlayStart ? MeshStyle.Top : i < MemberCount && owners.Contains(i) ? MeshStyle.Top : rest;
        }
        else if (_board != null && ((Step == 2 ? _rvFormLine != null : _fwPicked != null) || _fwPickedSet.Count > 0))
        {
            // 고른 줄(② 대응표 · ③ 거푸집 줄): 통째로 그 줄인 덩이와 덧그린 몫(다른 줄과 이은 판은 옅게 · 윤곽선 — 그 줄 몫만 진하게) · 고른 판(R66).
            var inRow = _fwLineWhole;
            for (int i = 0; i < count; i++)
                styles[i] = i >= OverlayStart ? MeshStyle.Top
                    : i >= MemberCount && (inRow.Contains(i - MemberCount) || _fwPickedSet.Contains(i - MemberCount)) ? MeshStyle.Top : rest;
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
                _fwSel.Clear();
                _fwFaces.Clear();
            }
        }
        else _editor.Select(id > 0 && index >= 0 ? new[] { id } : Array.Empty<long>());
        RefreshAll();
        if (index >= 0) _view3d.ZoomTo(index);
    }

    // 판을 찍으면(R66 · R73a 면 단위) 찍은 세모의 면을 고른다 — Ctrl 이면 더하거나 뺀다 · 그 면의 판도 골라 카드의 단추는 판 전체에(검토 M3) ·
    // 위치 나무가 그 줄까지 펼쳐진다. 부재 · 빈 곳은 그대로 둔다(Esc 로 푼다).
    private void ClickFormwork(int index, bool ctrl = false, int triangle = -1)
    {
        if (_board == null || index < MemberCount || index >= OverlayStart) return;
        if (FaceAtClick(index - MemberCount, triangle) is { } face) SelectFaces(new[] { face }, ctrl ? SelectMode.Toggle : SelectMode.Replace, fly: false);
    }

    // 표(«유형 · 부위별로 한꺼번에»)에서 줄을 골랐다(또는 Esc 로 풀었다): 고른 판은 푼다. 줄을 고르면 그 줄의 판이 다 보이게 화면을 맞춘다(JACK 2026-10-21 «포커싱도»).
    private void PickRow((string type, string group)? key, string? sub = null)
    {
        _fwPicked = key;
        _fwPickedSub = key == null ? null : sub;
        _fwSel.Clear();
        _fwFaces.Clear();                         // 고른 면도(R73a — Esc 도 이 길)
        if (_fwIso != null) _fwIsoEndWhy = key == null ? "Esc" : "표 줄 고름";
        _fwIso = null;                            // 부재만 보기도(R73a2 — Esc = 모두 보기 + 풂)
        _fwPlaceOpen = false;
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
            // Revit 에서도 그 줄 몫의 부재(R61a — 거름으로 숨긴 판의 부재도 · 상자는 보이는 몫).
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
        // 직접 정한 판(R66 — 판 종류 · 자리 · 쪼갬 · 옮김) · 줄 규칙이 정한 판(사용자 모드의 «유형 · 부위별로 한꺼번에»).
        string moved = (FormworkBoard.Direct(piece) ? "직접 · " : _board != null && _board.RowRuled(piece) ? "줄 규칙 · " : "")
                       + (lead.MovedTo is { } to ? $"옮김 → {RowLabel(to)} · " : "");
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

    // ── 되돌리기 · 다시 그리기(R55b · R66) ──
    // ② 에서 한 일(마지막이 끝) — 이름 한 줄 · 되돌리는 일(돌려주는 값 = 다시 셌다). R66 부터 기록 글 전체를 떠 두었다가 그대로 다시 쓴다(모드까지).
    // 창을 닫으면 없어진다.
    private readonly List<(string what, Func<bool> undo)> _fwUndo = new();

    // 기록 글을 떠 둔 되돌리기 하나(R66). 되돌릴 때의 판에 다시 쓴다 — ① 에서 고쳐 판을 다시 지어도 기록 글은 그대로 쓸 수 있다(설치 전 검토 M1).
    private void RecordUndo(string what, string snapshot) =>
        _fwUndo.Add((what, () =>
        {
            if (_board is not { } board) return false;
            board.RestoreSnapshot(snapshot);
            return true;
        }));

    // 다시 센 뒤: 겉면 갈아 끼우기 · 파일에 적기 · 일지 · 알림(자동 모드에서 고쳐 사용자 모드로 바뀌었으면 함께 — R66 계획 검토 H3 · M3).
    private void FinishChange(string log, string said, string what)
    {
        if (_board == null) return;
        string problem = SaveRules?.Invoke() ?? "";
        var watch = Stopwatch.StartNew();
        bool switched = _board.ModeSwitched;
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke(log + $" · 모드 {(_board.UserMode ? "사용자" : "자동")}{(switched ? "(자동에서 바뀜)" : "")} · 직접 정한 판 {DirectCount()}" +
                    $" · 지운 판 {_board.DroppedCount}장({M2(_board.DroppedArea)} ㎡) · 옮긴 판 {_board.MovedCount}장 · 모양이 바뀌어 다시 센 판 {_board.LostDrops.Count}" +
                    $" · 일부만 찾음 {_board.DropCheck.Partial.Count} · 넓이 달라짐 {_board.DropCheck.Resized.Count} · 거푸집 {M2(_board.Total)} ㎡" +
                    $" · 되돌릴 일 {_fwUndo.Count} · 판 다시 지음 {_board.RefreshMilliseconds} ms · 형상 다시 잼 {_board.MeasureVersion}번(마지막 {_board.RemeasureMilliseconds} ms)" +
                    $" · 겹침 기록 {_board.Rules.JointCount} · 그림 {watch.ElapsedMilliseconds} ms");
        if (switched) said = "사용자 모드로 바꿨습니다 · " + said;
        if (problem.Length > 0) ShowMessage($"{what} — 기록을 모델에 적지 못했습니다(이 창에서는 바꾼 대로 셉니다): {problem}", true);
        else ShowMessage(said);
    }

    // [되돌리기] · Ctrl+Z(② — R55b): ② 에서 한 일을 마지막부터 하나씩.
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
        var tabs = BuildSideTabs();               // [판 | 규칙](R71)
        DockPanel.SetDock(tabs, Dock.Top);
        dock.Children.Add(tabs);
        // [자동 | 사용자](R66) · 거름 · 위치 나무(R73a — 판 목록 · 지운 판 묶음 대신) · 맨 아래 유형 · 부위별로 한꺼번에(사용자 모드 — 옛 대응표).
        var plates = BuildTreePane();
        plates.Visibility = _fwRulesOpen ? Visibility.Collapsed : Visibility.Visible;
        _fwPlatesPane = plates;
        var host = new Grid();
        host.Children.Add(plates);
        host.Children.Add(BuildRulesPane());      // 칠 때 · 짝 예외(R71 — 시공 순서표)
        dock.Children.Add(host);
        return dock;
    }

    // 옛 대응표의 칸 머리(면 · 넓이 · 종류) — 접은 칸 안에 둔다.
    private Grid RuleColumns()
    {
        var columns = RowGrid();
        columns.Margin = new Thickness(8, 4, 4, 4);
        var titles = new[] { ("면", HorizontalAlignment.Left), ("넓이(㎡)", HorizontalAlignment.Right), ("종류", HorizontalAlignment.Left) };
        for (int c = 0; c < titles.Length; c++)
        {
            var label = new TextBlock { Text = titles[c].Item1, Foreground = _sub, FontSize = 12, HorizontalAlignment = titles[c].Item2, Margin = new Thickness(c == 1 ? 0 : c == 2 ? 2 : 0, 0, c == 1 ? 10 : 0, 0) };
            Grid.SetColumn(label, c);
            columns.Children.Add(label);
        }
        return columns;
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
        string snapshot = _board.Snapshot();
        if (!_board.SetSubRule(key.type, key.group, sub, choice)) return false;
        var label = RowOf(key) is { } r && r.Subs.FirstOrDefault(s => s.Key == sub) is { } s ? SubLabel(r, s) : sub;
        string name = choice.Length > 0 ? choice : "줄 값 · 자동";
        RecordUndo($"{RowLabel(key)} · {label} → {name}", snapshot);
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        _fwPickedSub = sub;
        _fwSel.Clear();
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
        string snapshot = _board.Snapshot();
        if (!_board.SetRule(key.type, key.group, choice)) return false;
        // 되돌리기 목록(R55b · R66): 기록 글 전체(아래 줄 값 · 모드까지).
        if (record) RecordUndo($"{RowLabel(key)} → {(choice.Length > 0 ? choice : "자동")}", snapshot);
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        _fwPickedSub = null;
        _fwSel.Clear();                            // 표의 줄을 고르면 고른 판은 푼다(R66)
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

    // 모양이 바뀌어 찾지 못한 뺀 판의 기록을 지운다(revit-verify R48 설치 전 M1): 대응표를 모델에 적고 화면을 맞춘다(ChangeRule 과 같은 길).
    private bool ForgetLostDrops()
    {
        if (_board == null || Busy) return false;
        var leads = _board.LostDrops.ToList();
        string snapshot = _board.Snapshot();
        // 찾지 못한 판 종류 기록도(R57 P3 — 계획 검토 L9).
        int n = _board.ForgetLostDrops() + _board.ForgetLostKinds();
        if (n == 0) return false;
        RecordUndo($"찾지 못한 판 기록 지우기 {n}장", snapshot);
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

    // 그림 위: 거푸집 합계 · 확인할 자리 + 경고 + 종류별 넓이(딱지) + 고른 판의 띠(R66 — PlacePartView.Plates · 골랐을 때만).
    private FrameworkElement BuildFormworkTop()
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_fwTotal);
        _ui.Mark(_fwWarnings, "", "fw.warnings");
        texts.Children.Add(_fwWarnings);
        texts.Children.Add(_fwKinds);
        _fwPlateBar = BuildPlateBar();
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
            Text = hintText ?? "굴림 = 확대 · 가운데 끌기 = 옮기기 · 오른쪽 끌기 = 돌리기 · 찍기 = 면 고르기(Ctrl = 더하기 · 끌기 = 네모로) · Del = 숨기기 · Ctrl+Z = 되돌리기",
            Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(hint, Dock.Right);
        box.Children.Add(hint);
        // 좁은 창(1280 — R73a)에서도 색 풀이가 한 줄 폭(210)은 남게 쓰는 법 글을 줄인다(«…» · 다 읽기는 풍선 글).
        _ui.Tip(hint, hint.Text);
        box.SizeChanged += (_, e) => hint.MaxWidth = Math.Max(160, e.NewSize.Width - 210);
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
        // 풀이 글은 둘로(좁은 칸에서 하나가 잘리지 않고 다음 줄로 — R73a).
        legend.Children.Add(new TextBlock { Text = "연한 색 = 안쪽 면", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 2) });
        legend.Children.Add(new TextBlock { Text = "분홍 테두리 = 고른 면", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
        box.Children.Add(legend);
        return box;
    }

    // ② 의 화면을 지금 값으로 맞춘다: 줄의 바탕 · 넓이 · 초안 · 종류 칸 · 합계 · 아래 줄의 글.
    // 줄의 넓이와 초안은 바뀔 수 있다(판을 빼면 그 조각이 넓이에서 빠진다 · 붙은 벽의 종류가 바뀐다 — R47 · R48). 줄의 모임은 그대로다.
    private void RefreshFormwork()
    {
        if (_board == null) return;
        // 고른 줄 · 고른 면(R73a)이 바뀌었으면 덧그림을 다시 짓는다(겉면 갈아 끼우기 — 대응표 줄 · 면을 누르거나 풀 때) · 아니면 모습만.
        if (HiddenKey() != _fwMaskKey) SetFormworkMeshes();   // 숨기거나 되보이면(R73a3) 이은 판의 숨긴 몫이 바뀐다
        else if (!Equals(OverlayKey(), _fwOverlayFor) || !_fwFaces.SequenceEqual(_fwFaceOverlayFor, ReferenceEqualityComparer.Instance)
                 || SoloWhole != _fwFaceOverlayWhole) SetFormworkMeshes(selectionOnly: true);
        else
        {
            UpdateFormworkLook();
            if (HiddenKey() != _fwMaskKey) SetFormworkMeshes();         // 이 부재만 보기가 무리 밖을 골라 끝났다(R73a4) — 판의 뺀 몫 · 면 겉면을 모두 보기로
            else if (ClipsChanged) SetFormworkMeshes(selectionOnly: true);   // 부재만 보기를 켜고 끄면(R73a3) 잘라 그린 몫이 바뀐다 — 겉면을 갈아 끼운다
        }
        RefreshPlateList();                        // 모드 · 접은 칸(R66)
        RefreshTree();                             // 위치 나무 · 거름(R73a)
        if (SoloOn) RefreshIsoBar();               // 띠의 부재 이름 = 나무 줄 이름(R73a4 — 다시 잰 뒤 새 나무로)
        RefreshRulesPane();                        // 규칙 탭(R71 — 열려 있을 때만)
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
        foreach (var issue in AllIssues()) _fwWarnings.Children.Add(IssueChip(issue, new Thickness(0, 4, 6, 0)));   // 모델의 문제(R63b) — 누르면 그 부재
        if (gaps > 0)
            _fwWarnings.Children.Add(WarnChip($"그림에 없는 틈 {gaps}곳",
                "틈 = 아래 콘크리트와 너무 가까워 거푸집을 댈 수 없는 밑면입니다(세지 않음 · 대응표 줄이 없어 3D 에 칠하지 않습니다).", "fw.warn.gaps"));
        if (splits > 0)
            _fwWarnings.Children.Add(WarnChip($"종류가 달라 못 이은 면 {splits}곳",
                "벽과 슬래브 옆처럼 같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 자리입니다. 종류를 맞추면 한 줄이 됩니다.\n"
                + string.Join("\n", _board.Merge.SplitSummary(5).Select(s => "  " + s)), "fw.warn.splits"));   // 풍선 글은 굴리지 않는다 — 앞 다섯 가지만(전부는 일지)
        // 사람이 지운 판(R48 — 1021 «지우기»): 합계 · 엑셀 · 거푸집 객체에서 빠진 것을 잊지 않게 · 옮긴 판(R55b) · 옮길 줄이 없어진 판 · 모양이 바뀌어 다시 센 판.
        int dropped = _board.DroppedCount, lost = _board.LostDrops.Count + _board.KindCheck.Lost.Count, moved = _board.MovedCount, orphans = _board.OrphanCount;
        int directPlates = DirectCount();
        if (dropped > 0)
            _fwWarnings.Children.Add(WarnChip($"세지 않는 판 {dropped}장 · {M2(_board.DroppedArea)} ㎡",
                "판마다 «세지 않음»으로 정한 판입니다 — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다(부재 속성(지침용) 합계에는 들어 있습니다). 판을 고르고 [↺ 자동으로] · 종류 단추.", "fw.warn.dropped"));
        if (moved > 0)
            _fwWarnings.Children.Add(WarnChip($"옮긴 판 {moved}장 · {M2(_board.MovedArea)} ㎡",
                "전 판에서 다른 줄로 옮긴 판입니다 — 그 줄 · 그 종류로 셉니다(엑셀 · 거푸집 객체도). 부재 속성(지침용)은 옮기기 전 종류 그대로. 판을 고르고 [↺ 자동으로] · [자리 바꾸기].\n"
                + string.Join("\n", _board.Rules.Moves.Take(5).Select(m => "  → " + RowLabel(m.to))) + (moved > 5 ? "\n  …" : ""), "fw.warn.moved"));
        if (directPlates > 0)
            _fwWarnings.Children.Add(WarnChip($"직접 정한 판 {directPlates}장",
                "사용자 모드에서 판마다 종류 · 자리 · 나누기 · 합치기를 정한 판입니다(Revit 판의 종류 칸에서 고친 판도). 판을 고르고 [↺ 자동으로]로 규칙대로 돌립니다.", "fw.warn.kinds"));
        // 자동 모드인데 모델에 사람이 정한 기록이 있다(R66 — 계획 §4 «기록은 지우지 않고 한 줄»): 쓰지 않고 남겨 둔다 — [사용자]로 바꾸면 다시 걸린다.
        if (!_board.UserMode && _board.Rules.HasRecords)
            _fwWarnings.Children.Add(WarnChip("직접 정한 기록 쓰지 않음",
                "자동 모드라 사용자 모드에서 정한 줄 · 판 기록을 쓰지 않고 규칙대로 셉니다. 기록은 모델에 남아 있어 [사용자]로 바꾸면 다시 걸립니다.", "fw.warn.autoKept"));
        if (orphans > 0)
            _fwWarnings.Children.Add(WarnChip($"옮길 줄이 없어진 판 {orphans}장",
                "옮긴 줄이 표에서 없어져(① 에서 부위 이름을 바꿈 · 줄이 가려짐) 세지 않는 판으로 돌아갔습니다. 판을 고르고 종류를 정하세요.", "fw.warn.orphan"));
        if (lost > 0)
        {
            // 기록을 지울 단추를 곁에 둔다(revit-verify R48 설치 전 M1 — 다시 찍어 빼도 옛 기록이 남아 경고가 영영 뜬다).
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(WarnChip($"모양이 바뀌어 다시 센 판 {lost}장",
                "전에 판마다 정한 자리를 이번 모양에서 찾지 못해 그 자리를 규칙대로 셉니다(부재를 지웠거나 · 나중에 침 · 결합으로 면이 크게 바뀐 때). 필요하면 다시 정하세요.", "fw.warn.lost"));
            var forget = _ui.Button("기록 지우기", "BtnGhost", "찾지 못한 판의 기록을 지웁니다(그 자리는 이미 규칙대로 세고 있어 합계는 그대로입니다).", "btn.fw.forgetLost");
            forget.Focusable = false;
            forget.Margin = new Thickness(0, 4, 6, 0);
            forget.VerticalAlignment = VerticalAlignment.Center;
            forget.Click += (_, _) => ForgetLostDrops();
            line.Children.Add(forget);
            _fwWarnings.Children.Add(line);
        }
        // 찾았지만 모양이 바뀐 뺀 판(core-verify R48 설치 전 M3): 일부 조각만 찾음 · 빠진 넓이가 뺄 때와 5% 넘게 다름.
        var check = _board.DropCheck;
        // 판 종류를 정한 판이 지금 둘 이상으로 나뉨(R68) · 층마다 갈려 넓이가 바뀜(R70)도 — 한 기록은 한 번.
        int kindPartial = _board.KindCheck.Partial.Union(_board.SplitKinds, StringComparer.Ordinal).Union(_board.KindCheck.Resized, StringComparer.Ordinal).Count();
        var changedLeads = check.Partial.Concat(check.Resized).Distinct().ToList();
        if (changedLeads.Count > 0)
            _fwWarnings.Children.Add(WarnButton($"⚠ 모양이 바뀐 판 {check.Changed + kindPartial}",
                "전에 세지 않기로 한 판인데 모델이 바뀌어 넓이가 달라졌습니다. 누르면 그 판(여럿이면 차례로) — 다르면 [↺ 자동으로] 뒤 다시 정하세요.",
                "fw.warn.changed", () => PickDropped(changedLeads[_changedCycle++ % changedLeads.Count]), new Thickness(0, 4, 6, 0)));
        else if (kindPartial > 0)
            _fwWarnings.Children.Add(WarnChip($"모양이 바뀐 판 {kindPartial}장",
                (check.Partial.Count > 0 ? $"일부 조각만 찾은 판 {check.Partial.Count}장 — 찾은 조각만 빠지고 나머지는 다시 셉니다.\n" : "") +
                (_board.KindCheck.Partial.Count > 0 ? $"일부 조각만 찾은 판 종류 {_board.KindCheck.Partial.Count}장 — 찾은 조각만 그 종류이고 나머지는 규칙의 종류입니다.\n" : "") +
                (_board.SplitKinds.Count > 0 ? $"종류를 정한 판이 둘 이상으로 나뉨 {_board.SplitKinds.Count}장 — 판을 나누는 규칙이 바뀌었습니다(정한 종류가 나뉜 판 모두에 그대로).\n" : "") +
                (_board.KindCheck.Resized.Count > 0 ? $"종류를 정한 판이 층마다 갈림 {_board.KindCheck.Resized.Count}장 — 정한 종류는 한 층 조각에만 남습니다.\n" : "") +
                (check.Resized.Count > 0 ? $"빠진 넓이가 뺄 때와 다른 판 {check.Resized.Count}장 — 이웃 부재가 바뀌어 판이 커지거나 줄었습니다.\n" : "") +
                "판을 골라 넓이를 보고, 다르면 [↺ 자동으로] 뒤 다시 정하세요.", "fw.warn.changed"));
        // 부위 줄에 가려진 옛 유형 줄(core-verify R48 설치 전 M1): 부위를 넣은 부재의 면은 부위 줄만 정한다 — 전에 고친 유형 줄이 표에 없고 걸리지도 않는다.
        var hidden = _board.HiddenTypeRules;
        if (hidden.Count > 0)
            _fwWarnings.Children.Add(WarnChip($"가려진 줄 {hidden.Count}",
                "고친 줄인데 그 면이 다른 줄에서 정해집니다 — 부위를 넣은 부재의 면은 부위 줄, 벽과 한 평면으로 이어진 면(한 덩어리)은 벽 줄. 이 줄은 걸리지 않습니다(기록에는 남음).\n"
                + string.Join("\n", hidden.Take(5).Select(h => $"  {h.type} · {h.group} → {h.item}")) + (hidden.Count > 5 ? "\n  …" : ""), "fw.warn.hidden"));
        // 판 모양 검사(R68 — JACK 2026-10-28 «이런 거푸집이 있을수있어? · 기하 모양을 검증하는 시스템» · 고름 «② 판 목록 ⚠ + 3D 빨강»): 검사마다 «⚠ 이름 수» —
        // 누르면 그 판들을 고르고 화면을 맞춘다(고른 판의 걸린 자리는 3D 에 빨간 실선). 섞임은 정보(ⓘ — 외벽 덩어리처럼 2D 와 같은 경우가 많다).
        foreach (var test in Enum.GetValues<FormworkShapeCheck.Test>())
        {
            var hit = _board.PiecesWith(test);
            if (hit.Count == 0) continue;
            string name = FormworkShapeCheck.NameOf(test);
            string tip = ShapeTip(test) + "\n" + string.Join("\n", hit.Take(6).Select(i => "  " + PieceText(_board.Pieces[i]) + " — " +
                string.Join(" · ", _board.FindingsOf(_board.Pieces[i]).Where(f => f.Test == test).Select(f => f.Text)))) + (hit.Count > 6 ? "\n  …" : "");
            if (FormworkShapeCheck.IsInfo(test))
            {
                var chip = _ui.Button($"ⓘ {name} {hit.Count}", "BtnGhost", tip, "fw.check." + test);
                chip.Focusable = false;
                chip.MinHeight = 23;
                chip.Padding = new Thickness(8, 0, 9, 1);
                chip.Margin = new Thickness(0, 4, 6, 0);
                chip.Click += (_, _) => SelectPieces(hit, SelectMode.Replace, zoom: true);
                _fwWarnings.Children.Add(chip);
            }
            else _fwWarnings.Children.Add(WarnButton($"⚠ {name} {hit.Count}", tip, "fw.check." + test, () => SelectPieces(hit, SelectMode.Replace, zoom: true), new Thickness(0, 4, 6, 0)));
        }
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
                       (dropped > 0 ? $" · 세지 않는 판 {dropped}장" : "") + (moved > 0 ? $" · 옮긴 판 {moved}장" : "") + (orphans > 0 ? $" · 옮길 줄이 없어진 판 {orphans}장" : "") +
                       (lost > 0 ? $" · 모양이 바뀌어 다시 센 판 {lost}장" : "") + "\n" + string.Join(" · ", parts);
        _status.Text = _board.UserMode
            ? $"사용자 모드 · 직접 정한 판 {directPlates}장" + (_board.Rules.Count > 0 ? $" · 줄 규칙 {_board.Rules.Count}" : "") + " · 고칠 때마다 모델에 적힙니다"
            : "자동 모드 · 규칙대로 셉니다";
        // [되돌리기](② 에서 한 일 — R55b): 할 것이 없으면 꺼짐 · 풍선 글 = 되돌릴 일.
        _undo.IsEnabled = _fwUndo.Count > 0;
        _ui.Tip(_undo, _fwUndo.Count > 0 ? $"되돌리기(Ctrl+Z): {_fwUndo[^1].what}" : "되돌릴 것이 없습니다.");
        RefreshPlateBar();                         // 고른 판의 띠(R66)
        ShowFaceMarks();                           // 고른 면 위 이름 · 치수 · 판 모양 검사 선 · 평면 지도(R73a)
    }

    // «모양이 바뀐 판» 딱지(R63b)가 지운 판을 차례로 고른다: 3D 에서 그 판을 찍고 그 판이 보이게 화면을 맞춘다 · 나무는 그 판의 첫 면까지 펼친다(R73a — 전의 «지운 판» 줄 대신).
    private void PickDropped(string lead)
    {
        if (_board == null || Busy) return;
        int at = _board.IndexOfDrop(lead);
        if (at < 0) return;
        var piece = _board.Pieces[at];
        _fwSel.Clear();
        _fwSel.Add(piece.Lead);
        _fwFaces.Clear();
        _fwPicked = null;
        _fwPickedSub = null;
        _fwPlaceOpen = false;
        _rvMembers.Clear();
        RefreshAll();
        // 보기 거르기(종류 · 층 · 숨긴 부재)로 빠진 판이면 화면은 그대로 둔다 — 안 보이는 판으로 맞추지 않는다(설치 전 검토 L2 · PickRow 와 같이).
        if (!piece.Mesh.Box.IsEmpty && FormworkShown(MemberCount + at)) _view3d.ZoomTo(piece.Mesh.Box, margin: RowZoomMargin, minWidth: RowZoomMinWidth);
        FollowPiece(piece);   // R61a
        if (_board.FacesOfPiece(at).FirstOrDefault() is { } face) RevealFace(face);
    }

    // 종류 칸이 바꾸는 범위(대응표의 그 줄): 부위 줄 = 그 부위 전체 · 유형 줄 = 그 유형의 그 면.
    private static string ScopeText(FormworkRow row) =>
        FormworkRules.PartOf(row.Type) is { } part ? $"부위 {part} 전체" : $"{row.Type} 의 {row.Group}";

    private static string M2(double v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // 판 모양 검사의 풀이(풍선 글 — R68).
    private static string ShapeTip(FormworkShapeCheck.Test test) => test switch
    {
        FormworkShapeCheck.Test.Notch => "폭 0.5 m 보다 좁고 깊은 홈 · 틈이 난 판 — 다른 부재가 판을 가로지른 자리일 수 있습니다. 누르면 그 판.",
        FormworkShapeCheck.Test.Thin => "폭 5 cm 미만 띠가 붙은 판 — 거푸집을 대기 어려운 모양입니다. 누르면 그 판.",
        FormworkShapeCheck.Test.Small => "0.05 ㎡ 미만 — 이웃 콘크리트가 덮고 남은 부스러기 판입니다. 누르면 그 판.",
        FormworkShapeCheck.Test.Mixed => "부위가 둘 이상이거나 벽을 따라 종류를 받은 조각이 든 판 — 외벽(슬래브 옆 + 벽)처럼 2D 와 같은 경우가 많아 정보로만 보입니다.",
        _ => "식이 가로 × 세로 꼴로 나오지 않거나(넓이만) 네모에서 반 넘게 빈 판(떨어진 조각이 한 판 등). 누르면 그 판.",
    };

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

    /// <summary>그 줄의 종류를 골랐다(접은 표 «유형 · 부위별로 한꺼번에»의 칸을 바꾼 것과 같은 길 — 기다리지 않고 바로).</summary>
    public bool ChooseKind(string type, string group, string choice) => ChangeRule((type, group), choice);
    /// <summary>3D 에서 그 덩이를 찍었다(덩이의 차례 · Ctrl — 찍은 세모를 모르니 그 판의 대표 면).</summary>
    public void ClickPiece(int piece, bool ctrl = false) => ClickFormwork(MemberCount + piece, ctrl);
    /// <summary>3D 에서 끌어 네모로 골랐다(덩이의 차례들 · Ctrl — 화면의 끌기와 같은 길).</summary>
    public void BandPieces(IEnumerable<int> pieces, bool ctrl = false) => BandFormwork(pieces.Select(p => MemberCount + p).ToList(), ctrl);
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
    /// <summary>그 덩이가 흰 막으로 덮여 있는가(고른 판).</summary>
    public bool PieceHighlighted(int piece) => FormworkPicked(MemberCount + piece);
    /// <summary>그 덩이 · 부재를 그리는 방식(고른 줄 · 판이면 맨 위, 나머지 = 옅게).</summary>
    public MeshStyle PieceStyle(int piece) => FormworkStyle(MemberCount + piece);
    public MeshStyle MemberStyle(int member) => FormworkStyle(member);
    /// <summary>띠의 첫 판(고른 판의 마지막)의 덩이 차례(없으면 −1).</summary>
    public int PickedPiece => _fwPickedIndex >= 0 ? _fwPickedIndex - MemberCount : -1;
    /// <summary>고른 판들의 덩이 차례(R66).</summary>
    public IReadOnlySet<int> PickedPieces => _fwPickedSet;
    /// <summary>고른 판의 띠 글(띠가 없으면 "").</summary>
    public string PlateBarText => _fwPlateBar is { Visibility: Visibility.Visible } ? _fwPlateText.Text : "";
    /// <summary>② 에서 되돌릴 수 있는 일의 수.</summary>
    public int FormworkUndoCount => _fwUndo.Count;
    /// <summary>[되돌리기] 단추(① · ② 같이 씀).</summary>
    public Button UndoButton => _undo;
    /// <summary>고른 판의 띠 종류 단추(종류 이름 · «세지 않음» · ""= ↺ 자동으로) — 보이지 않으면 null.</summary>
    public Button? PlateKindButton(string item)
    {
        if (_fwPlateBar is not { Visibility: Visibility.Visible }) return null;
        var button = item.Length == 0 ? _fwAutoButton : item == FormworkRules.NotCounted ? _fwDropButton : _fwKindButtons.GetValueOrDefault(item);
        return button is { Visibility: Visibility.Visible } ? button : null;
    }
    /// <summary>고른 판의 띠 단추(«split» 부재마다 나누기 · «merge» 합치기 · «place» 자리 바꾸기 · «placeApply» 자리 적용) — 보이지 않으면 null.</summary>
    public Button? PlateActionButton(string name)
    {
        if (_fwPlateBar is not { Visibility: Visibility.Visible }) return null;
        var button = name switch { "split" => _fwSplitButton, "merge" => _fwMergeButton, "place" => _fwPlaceButton, "placeApply" => _fwPlaceApply, _ => null };
        return button is { Visibility: Visibility.Visible } && (name != "placeApply" || _fwPlaceOpen) ? button : null;
    }
    /// <summary>고른 판을 세지 않음([세지 않음] 단추와 같은 길 — R73a3 부터 Del 은 숨기기).</summary>
    public bool DropPicked() => DropSelected();
    /// <summary>[자동] · [사용자] 칸(R66).</summary>
    public RadioButton? ModeButton(bool user) => user ? _fwUserMode : _fwAutoMode;
    /// <summary>자리 바꾸기 칸의 부위 · 중분류 고르는 칸(열려 있을 때만).</summary>
    public ComboBox? PlacePartBox => _fwPlaceOpen ? _fwPlacePart : null;
    public ComboBox? PlaceZoneBox => _fwPlaceOpen ? _fwPlaceZone : null;
}
