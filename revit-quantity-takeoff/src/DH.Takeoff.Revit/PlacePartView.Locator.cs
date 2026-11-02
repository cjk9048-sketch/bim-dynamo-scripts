using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73a5 — «이 부재만 보기» 동안 3D 를 둘로(JACK 1102 «2분할로 돼서 한쪽은 윤곽선에 그 객체만 보이는 거, 하나는 지금 만드는 거 보이는 걸로»).
// R73a6 — JACK 1102 «목록에서 선택 시 분할 화면 중 전체 화면은 해당 객체가 화면에 꽉 차게 줌인되게 해 줘. 그리고 뷰 순서를 바꿔 줘 — 전체 뷰가 오른쪽으로 오게.
// 그리고 면을 어느 뷰든 면을 선택하면 두 뷰 모두 그 면이 선택되게 해 줘»:
//  · 왼쪽 = 부재 단독 보기(지금 것 그대로 — 빗금 · 딱지 · [공제] · [공제 제외] · 보기 단추 · 띠) · 오른쪽 = «전체 모델» · 처음 폭 55 : 45(왼쪽 도구 · 보기 단추가 한 줄에 들게 · 왼쪽 가장 좁은 폭 480).
//  · 전체 모델: 나머지 부재 = 화면 선 윤곽(R73a5 그대로) · 고른 부재 = 왼쪽과 같은 그림(회색 몸 + 거푸집 판 종류 색 · 고른 면 분홍 테두리 · 공제 위치 빗금 + 이웃 이름 딱지).
//    고른 면의 분홍 테두리는 화면 선(SetPickLines — 3D 맨 위 층을 하나 더 그리면 녹촌 한 장이 45 ms 늘어 ② 첫 화면의 1.3 배를 넘었다 · 테두리 = 조각을 합친 모양).
//    나무 줄 · 면 고르기마다 고른 부재가 꽉 차게(한쪽 여백 12 % · OrbitCamera.Frame): 부재 줄 = 왼쪽이 날아간 방향 그대로(양쪽에서 같은 면 · 같은 빗금이 보이게 — 내 판단) ·
//    면 = 지금 방향(그 면이 등을 돌렸으면 FacingAngles 로 돌림) + 가운데를 그 면 쪽으로(부재가 화면 밖으로 나가지 않는 만큼). 막 나눴으면 바로(JumpTo) · 아니면 날아감.
//  · 찍기(한 길 — SelectFaces): 고른 부재의 판 = 그 면(왼쪽 찍기와 같음 · Ctrl = 더하기 · 빼기) · 다른 부재 = 그 부재 겉면 위 점의 면(FaceAtPoint · 보이는 겉면 먼저 —
//    고른 부재의 몸이 앞이면 그 몸) → 그 부재 단독 보기 + 그 면(나무 면 줄과 같은 길) · 거푸집 면이 아니면 그 부재 통째(나무 부재 줄과 같은 길) · 빈 곳 = 그대로.
//    마우스 밑 = 왼쪽과 같은 굵은 테두리 + 아래 이름표(같은 글). 끌어 고르기 없음. [공제] · [공제 제외] 도구는 왼쪽에서만 — 든 채 오른쪽을 누르면 고르기(도구는 든 채 · 내 판단).
//    글자판은 늘 왼쪽 그림이 갖는다(누르면 왼쪽으로 — Esc · Del · Ctrl+Z 가 그대로).
//  · 오른쪽 위 [맞춤] = 고른 부재에 다시 맞춤 · 가운데 단추 두 번 = 모델 전체(전처럼).
public sealed partial class PlacePartView
{
    private Model3DView? _locator;                            // 오른쪽 «전체 모델»(R73a6 — 전 R73a5 왼쪽 «위치»)
    private Grid? _split;                                     // [부재 단독 보기 | 가름대 | 전체 모델]
    private Grid? _locatorPane;
    private FrameworkElement? _splitter;
    private bool _locatorShown, _locatorFitPending;
    // 처음 폭 = 55 : 45(오른쪽 0.82 * — 왼쪽 도구 칸 + 보기 단추 · 띠가 한 줄에 들게) · 가름대로 바꾼 폭은 다시 나눌 때 그대로.
    private GridLength _locatorWidth = new(0.82, GridUnitType.Star);
    private const double SoloMinWidth = 480;                        // 왼쪽 3D 가장 좁은 폭(도구 칸 + 보기 단추 340 + 여백)
    private const double WholeMargin = 0.12, WholeMinWidth = 1.0;   // 전체 모델 맞춤: 한쪽 여백 12 % · 화면 가로 1 m 아래로는 좁히지 않음
    // 전체 그림 차례: 부재(MemberCount) → 고른 부재의 판(_wholePieces — 덩이 차례). 고른 면(_wholeFaces)은 겉면 없이 분홍 화면 선으로.
    private readonly List<int> _wholePieces = new();
    private readonly List<BoardFace> _wholeFaces = new();
    private Border? _wholePill;
    private readonly TextBlock _wholeHover = new() { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Text = " " };
    private BoardFace? _wholeHoverFace;
    private string? _wholeFramedFor, _wholeMarksFor;

    /// <summary>시험 · 일지용: 전체 모델 그림을 지은 ms(처음 한 번) · 마지막 맞춤 ms.</summary>
    public long LocatorBuildMilliseconds { get; private set; }
    public long WholeFrameMilliseconds { get; private set; }

    private int WholeFaceStart => MemberCount + _wholePieces.Count;

    // 3D 자리 = [지금의 3D 무대(부재 단독 보기) | 가름대 | 전체 모델(처음엔 폭 0 · 숨김)].
    private FrameworkElement BuildSplit(FrameworkElement stage)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });   // 나눌 때만 SoloMinWidth
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        var pane = new Grid { Visibility = Visibility.Collapsed, ClipToBounds = true };
        var title = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = true,
            Child = new TextBlock { Text = "전체 모델", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = _text },
        };
        _ui.Mark(title, "모델 전체에서 고른 부재의 위치 — 면을 누르면 양쪽 그림 모두 그 면을 고르고, 다른 부재를 누르면 그 부재로 바뀝니다(가운데 단추 두 번 = 모델 전체).", "fw.locator.title");
        pane.Children.Add(title);
        var fit = _ui.Button("맞춤", "BtnSeg", "고른 부재가 꽉 차게 다시 맞춥니다.", "btn.fw.locator.fit");
        fit.MinHeight = 26;
        fit.Padding = new Thickness(9, 0, 9, 1);
        fit.Focusable = false;
        fit.Click += (_, _) => FrameWhole(null, force: true);
        pane.Children.Add(new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = fit,
        });
        // 마우스 밑 이름표(왼쪽 아래 — 왼쪽 그림의 이름표와 같은 꼴 · 같은 글).
        _wholePill = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = _wholeHover,
        };
        pane.Children.Add(_wholePill);
        Grid.SetColumn(pane, 2);
        _ui.Mark(pane, "", "fw.locator");
        var splitter = new GridSplitter
        {
            Width = 6, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns, Background = _line, Focusable = false,
            Visibility = Visibility.Collapsed, ShowsPreview = false, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _ui.Mark(splitter, "끌어 두 그림의 폭을 바꿉니다.", "fw.locator.split");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(stage, 0);
        grid.Children.Add(stage);
        grid.Children.Add(splitter);
        grid.Children.Add(pane);
        _split = grid;
        _locatorPane = pane;
        _splitter = splitter;
        return grid;
    }

    // 나눔을 지금 것으로(RefreshIsoBar — 부재 단독 보기가 켜지고 꺼질 때마다): 켜지면 전체 모델 그림을 (처음이면 지어) 보인다 · 꺼지면 걷는다.
    // 그림의 겉면 · 색은 SyncWhole(RefreshFormwork 끝) · 맞춤은 FrameWhole(SelectFaces 끝 — 배치를 마친 뒤).
    private void ShowLocator(bool on)
    {
        if (_split == null || _locatorPane == null || _splitter == null || on == _locatorShown) return;
        _locatorShown = on;
        if (!on) _locatorWidth = _split.ColumnDefinitions[2].Width;
        _split.ColumnDefinitions[2].Width = on ? _locatorWidth : new GridLength(0);
        _split.ColumnDefinitions[2].MinWidth = on ? 200 : 0;
        _split.ColumnDefinitions[0].MinWidth = on ? SoloMinWidth : 240;
        _locatorPane.Visibility = _splitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _wholeFramedFor = _wholeMarksFor = null;
        if (on)
        {
            if (_locator == null) BuildLocator();
            // 처음 방향 = 왼쪽 3D 의 보는 방향(부재 줄은 FrameWhole 이 왼쪽이 날아갈 방향으로 맞춘다).
            _locator!.Camera.Yaw = _view3d.Camera.Yaw;
            _locator.Camera.Pitch = _view3d.Camera.Pitch;
            _locatorFitPending = true;
        }
        else HoverWhole(-1, -1);
        Log?.Invoke(on ? $"② 전체 모델 그림 켬\t부재 {_memberMeshes.Count} · 처음 지음 {LocatorBuildMilliseconds} ms" : "② 전체 모델 그림 끔");
    }

    // 전체 모델 그림(처음 한 번): 부재 = 화면 선 윤곽 · 고른 부재 = 몸 + 판 + 고른 면(SyncWhole 이 겉면을 넣는다) · 찍기 · 마우스 밑 · 글자판을 가져가지 않음.
    private void BuildLocator()
    {
        var watch = Stopwatch.StartNew();
        var view = new Model3DView(_memberMeshes, _ui.IsDark, layered: true)
        {
            Shown = WholeShown,
            ColorOf = WholeColor,
            Picked = _ => false,                   // 고른 면 = 분홍 화면 선(SyncWhole — SetPickLines)
            StyleOf = WholeStyle,
            GhostPickable = true,                  // 윤곽 부재도 그 겉면으로 찍는다(보이는 겉면 먼저 — SeenFirst)
            SeenFirst = true,
            BandSelect = false,
            Focusable = false,
            FlatGhosts = true,                     // 윤곽 = 화면 선(R73a5)
        };
        view.Clicked += (index, ctrl) => ClickWhole(index, ctrl);
        view.LabelClick = (index, ctrl) => ClickMarkLabel(index, ctrl, whole: true);   // 빗금 딱지 → 그 이웃의 맞닿은 면(R73a7 — 왼쪽과 같은 길)
        view.HoverMoved += (index, triangle) => HoverWhole(index, triangle);
        view.Hovered += index => { if (index < 0) HoverWhole(-1, -1); };
        view.Failed += ex => ShowMessage("3D 그림에서 오류가 났습니다(끌던 것은 그만두었습니다): " + ex.Message, true);
        view.PreviewMouseDown += (_, _) => _view3d.Focus();   // 글자판은 왼쪽 그림에(Esc · Del · Ctrl+Z 가 R73a5 처럼)
        _locator = view;
        _locatorPane!.Children.Insert(0, view);
        LocatorBuildMilliseconds = watch.ElapsedMilliseconds;
    }

    private bool WholeShown(int i) =>
        i < MemberCount ? !_editor.Hidden.Contains(_members[i].Id)
        : i < WholeFaceStart && FormworkShown(MemberCount + _wholePieces[i - MemberCount]);

    private (byte r, byte g, byte b) WholeColor(int i) =>
        i < MemberCount ? FormworkColor(i)
        : i < WholeFaceStart ? FormworkColor(MemberCount + _wholePieces[i - MemberCount]) : (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);

    // 고른 부재 · 그 판 = 보통 · 나머지 부재 = 윤곽.
    private MeshStyle WholeStyle(int i) => i < MemberCount && !SoloShows(_members[i].Id) ? MeshStyle.Ghost : MeshStyle.Solid;

    // 전체 모델 그림의 겉면을 지금 것으로(RefreshFormwork 끝): 부재(고른 부재는 덮인 모서리를 뺀 ② 겉면) + 고른 부재의 판(그 부재 몫 — PieceMesh) ·
    // 고른 면 = 분홍 화면 선(그 면 겉면의 테두리 — 왼쪽과 같은 FaceMesh). 겉면이 그대로면 색 · 보임만 다시(Refresh).
    private void SyncWhole()
    {
        if (!_locatorShown || _locator == null || _board == null || Step != 1) return;
        _wholePieces.Clear();
        _wholeFaces.Clear();
        if (SoloOn)
        {
            foreach (int piece in _fwIsoOwn.Order())
                if (piece < _board.Pieces.Count && FormworkShown(MemberCount + piece)) _wholePieces.Add(piece);
            if (!SoloWhole)
                foreach (var face in FocusFaces().DistinctBy(f => f.Siblings[0]))   // 한 면처럼 볼 면들은 테두리 하나(R73a7)
                    if (FaceShown(face)) _wholeFaces.Add(face);
        }
        var bodies = _fwMemberMeshes.Count == MemberCount ? _fwMemberMeshes : _memberMeshes;
        var meshes = new List<MeshData>(MemberCount + _wholePieces.Count);
        for (int i = 0; i < MemberCount; i++) meshes.Add(SoloShows(_members[i].Id) ? bodies[i] : _memberMeshes[i]);
        meshes.AddRange(_wholePieces.Select(PieceMesh));
        var lines = new List<(Vec3 a, Vec3 b)>();
        foreach (var e in _wholeFaces.Select(f => FaceMesh(f).E))
            for (int at = 0; at + MeshData.SegmentStride <= e.Length; at += MeshData.SegmentStride)
                lines.Add((new Vec3(e[at], e[at + 1], e[at + 2]), new Vec3(e[at + 3], e[at + 4], e[at + 5])));
        _locator.SetPickLines(lines);
        bool same = meshes.Count == _locator.MeshCount;
        for (int i = 0; same && i < meshes.Count; i++) same = ReferenceEquals(meshes[i], _locator.MeshAt(i));
        if (same) _locator.Refresh();
        else
        {
            _wholeHoverFace = null;
            _locator.SetHoverOutline(null);
            _locator.SetMeshes(meshes);
        }
    }

    // 나눔이 막 켜졌으면 배치를 마친다(왼쪽 3D 의 폭이 바뀐다 — 날아갈 자리를 새 폭으로 센다 · SelectFaces 가 왼쪽 날기 앞에 부른다).
    private void SettleSplit()
    {
        if (_locatorFitPending && _split != null) _split.UpdateLayout();
    }

    // 전체 모델을 고른 부재에 맞춘다(SelectFaces 끝 · [맞춤]): turn = 부재 줄일 때 왼쪽이 날아간 방향. 고른 부재 · 첫 면이 그대로면 다시 맞추지 않는다(사람이 돌린 자리를 지킨다).
    private void FrameWhole((double yaw, double pitch)? turn, bool force = false)
    {
        if (!_locatorShown || _locator == null || !SoloOn) return;
        bool first = _locatorFitPending;
        if (first)
        {
            _split?.UpdateLayout();
            _locatorFitPending = false;
        }
        var face = SoloWhole ? null : PrimaryFace;
        if (face != null && !FaceShown(face)) face = null;
        string key = string.Join(",", _fwIso!.Order()) + "|" + (face == null ? "" : $"{face.ElementId}:{face.Name}");
        if (!first && !force && key == _wholeFramedFor) return;
        var watch = Stopwatch.StartNew();
        if (WholeGoal(turn, face) is not { } goal || _fwIso == null) return;
        _wholeFramedFor = key;
        if (first) _locator.JumpTo(goal);
        else _locator.FlyTo(goal);
        WholeFrameMilliseconds = watch.ElapsedMilliseconds;
        Log?.Invoke($"② 전체 화면 맞춤\t부재 {string.Join(",", _fwIso.Order().Take(4))}{(_fwIso.Count > 4 ? " …" : "")}{(face != null ? " · " + face.Name : "")}{(first ? " · 처음" : force ? " · [맞춤]" : "")} · {WholeFrameMilliseconds} ms");
    }

    // 맞출 카메라: 고른 부재(보이는 것)의 상자가 꽉 차게 · 방향 = turn(부재 줄) 또는 지금 방향(면이 등을 돌렸으면 그 면 쪽으로) · 가운데를 첫 면 쪽으로.
    private OrbitCamera? WholeGoal((double yaw, double pitch)? turn, BoardFace? face)
    {
        if (_locator == null || _fwIso == null || _board == null) return null;
        var box = Box3.Empty;
        foreach (long id in _fwIso)
            if (SoloShows(id) && IndexOf(id) is int i && i >= 0) box = box.With(_memberMeshes[i].Box);
        if (box.IsEmpty) return null;
        var goal = _locator.Camera.Clone();
        if (face == null && turn is { } t) (goal.Yaw, goal.Pitch) = t;
        else if (face != null && !WholeSees(face, goal.Forward))
            (goal.Yaw, goal.Pitch) = SeeingAngles(face, (f, anchor, dir) => WholeSees(f, dir, anchor), goal.Yaw, goal.Pitch);   // 지금 방향에서 안 보이면 보이는 첫 후보(R73a6)
        goal.Frame(box, face != null ? face.Center - _board.Origin : null, WholeMargin, WholeMinWidth);
        return goal;
    }

    // 전체 모델에서 그 시선(dir)의 맨 앞이 그 면인가(R73a6 — 대표 점 · 판 = 그 세모의 면 · 몸 = 그 점의 면 · 윤곽 부재는 가리지 않는다).
    private bool WholeSees(BoardFace face, Vec3 dir, Vec3? at = null)
    {
        if (_locator == null || _board == null) return false;
        if (face.Face.Normal is { Length: 3 } nn && new Vec3(nn[0], nn[1], nn[2]).Dot(dir) > -0.2) return false;
        var anchor = at ?? (face.Patches.Where(p => p.Triangles.Count >= 3).OrderByDescending(p => p.Area).FirstOrDefault() is { } patch ? FormworkBoard.AnchorOf(patch) - _board.Origin : face.Center - _board.Origin);
        var origin = anchor - dir * 1000;
        var (index, triangle, distance) = _locator.HitRay(origin, dir);
        if (index < 0) return false;
        if (index >= MemberCount) return index < WholeFaceStart && ReferenceEquals(_board.FaceAt(_wholePieces[index - MemberCount], triangle), face);
        return ReferenceEquals(_board.FaceAtPoint(_members[index].Id, origin + dir.Unit() * distance + _board.Origin, dir), face);
    }

    // 전체 모델 그림의 그 차례 · 세모 · 화면 자리 → (면 · 부재 차례): 고른 부재의 판 = 그 세모의 면 · 고른 부재의 몸 = 면 없음(왼쪽처럼) ·
    // 다른 부재 = 그 겉면 위 점의 면(FaceAtPoint — 형상이 정해 세지 않는 면 · 못 찾으면 면 없음 = 부재 통째).
    private (BoardFace? face, int member) WholeAt(int index, int triangle, Point at)
    {
        if (_board == null || _locator == null || index < 0) return (null, -1);
        if (index >= MemberCount)
        {
            if (index >= WholeFaceStart) return (null, -1);
            int piece = _wholePieces[index - MemberCount];
            var face = _board.FaceAt(piece, triangle) ?? _board.FaceOf(_board.Pieces[piece].Lead);
            return (face, face != null ? IndexOf(face.ElementId) : -1);
        }
        // 부재 몸(고른 부재 · 다른 부재) = 그 점의 면(R73a6 — 다 덮인 면도 · 형상이 정해 세지 않는 면(윗면)은 없음 = 부재 통째).
        return (BodyFace(_locator, index, at), index);
    }

    // 전체 모델을 눌렀다(R73a6): 고른 부재의 면 = 그 면(왼쪽 찍기와 같은 길) · 다른 부재 = 그 부재 단독 보기 + 그 면(나무 줄과 같은 길) · 빈 곳 · 고른 부재의 몸 = 그대로.
    private void ClickWhole(int index, bool ctrl)
    {
        if (_board == null || _locator == null || Busy || Step != 1 || _locator.ClickMoved) return;
        var (face, member) = WholeAt(index, _locator.ClickTriangle, _locator.ClickPoint);
        if (member < 0) return;
        long id = _members[member].Id;
        if (SoloShows(id))
        {
            if (face == null) return;                  // 고른 부재의 윗면 등 — 왼쪽처럼 그대로
            Log?.Invoke($"② 면 고름(전체 화면)\t{MemberLabel(id)} · {face.Name}{(ctrl ? " · Ctrl" : "")}");
            SelectFaces(new[] { face }, ctrl ? SelectMode.Toggle : SelectMode.Replace, fly: false);
            return;
        }
        if (face != null)
        {
            Log?.Invoke($"② 면 고름(전체 화면)\t{MemberLabel(id)} · {face.Name} · 부재 바뀜{(ctrl ? " · Ctrl" : "")}");   // 다 덮인 면도(R73a6)
            SelectFaces(new[] { face }, ctrl ? SelectMode.Toggle : SelectMode.Replace, fly: true, isolate: true);
            return;
        }
        var faces = _board.FacesOfElement(id).Where(f => !f.Fixed).ToList();
        if (faces.Count == 0) { ShowMessage($"{MemberLabel(id)} — 거푸집 산출 대상 면이 없는 부재입니다."); return; }
        Log?.Invoke($"② 면 고름(전체 화면)\t{MemberLabel(id)} · 부재 전체 · 부재 바뀜{(ctrl ? " · Ctrl" : "")}");
        SelectFaces(faces, ctrl ? SelectMode.Add : SelectMode.Replace, fly: true, isolate: true, whole: true);
    }

    // 전체 모델의 마우스 밑(R73a6): 면이면 굵은 테두리 + 왼쪽과 같은 이름표 · 부재 몸이면 그 부재 글 · 빈 곳이면 접음.
    private void HoverWhole(int index, int triangle)
    {
        if (_locator == null || _wholePill == null) return;
        BoardFace? face = null;
        string text = "";
        if (index >= 0 && _board != null && Step == 1 && _locator.HoverPoint is { } at)
        {
            var (found, member) = WholeAt(index, triangle, at);
            face = found;
            text = found != null ? FaceText(found) : member >= 0 ? DescribeFormwork(member) : "";
        }
        if (!ReferenceEquals(face, _wholeHoverFace))
        {
            _wholeHoverFace = face;
            _locator.SetHoverOutline(face != null ? FaceMesh(face) : null);
        }
        _wholeHover.Text = text.Length > 0 ? text : " ";
        _wholePill.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 시험에서 쓰는 길 ──

    /// <summary>R73a5 · R73a6: 오른쪽 «전체 모델» 그림(나누지 않았으면 null · 지은 뒤 숨겼으면 숨긴 채) · 나뉘어 있는가 · 전체 모델 칸 · 가름대.</summary>
    public Model3DView? Locator => _locator;
    public bool LocatorShown => _locatorShown;
    public FrameworkElement? LocatorPane => _locatorPane;

    /// <summary>R73a6: 전체 모델의 판 차례 → 덩이 · 분홍 테두리를 그린 고른 면 수 · 마우스 밑 이름표(글 · 보이는가) · 그 화면 자리의 면.</summary>
    public IReadOnlyList<int> WholePieces => _wholePieces;
    public int WholeFaceCount => _wholeFaces.Count;
    public string WholeHoverText => _wholePill is { Visibility: Visibility.Visible } ? _wholeHover.Text : "";
    public BoardFace? WholeFaceAt(Point at) => _locator is { } v && v.HitAt(at) is var (index, triangle) ? WholeAt(index, triangle, at).face : null;
}
