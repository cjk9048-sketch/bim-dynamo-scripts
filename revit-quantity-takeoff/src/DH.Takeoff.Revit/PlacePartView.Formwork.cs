using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Run
using System.Windows.Input;
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
///  · 그림 위 = 거푸집 합계 · 종류별 넓이 · 확인할 자리 수 · 찍은 판의 띠([이 판 빼기] · [되살리기] — R48), 그림 아래 = 색 풀이.
///  · 3D 에서 판을 찍으면 흰 막 + 분홍 테두리로 덮고 그 판을 정한 줄을 고른다. 종류 · 층 거름은 ① 과 같이 쓴다(① 에서 숨긴 부재는 숨긴 채다).
/// </summary>
public sealed partial class PlacePartView
{
    private FormworkBoard? _board;
    private FrameworkElement? _fwSide, _fwTop, _fwBottom;
    private readonly StackPanel _fwRows = new();
    private readonly TextBlock _fwTotal = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly WrapPanel _fwKinds = new() { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
    // 경고 딱지(그림에 없는 틈 · 종류가 달라 못 이은 면): 합계 줄 끝에 이으면 좁은 창에서 «…»로 잘려 안 보인다 — 따로 줄바꿈되게 둔다(revit R43 R4).
    private readonly WrapPanel _fwWarnings = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<(string type, string group), (Border row, ComboBox combo, TextBlock area)> _fwRowViews = new();
    private (string type, string group)? _fwPicked;
    private string _fwTotalText = "";
    // 찍은 판(R48): 대표 조각으로 기억한다 — 대응표를 고치거나 판을 빼면 덩이를 다시 지어 차례가 바뀌지만 조각은 그대로다.
    private FacePatch? _fwPlate;
    private int _fwPickedIndex = -1;                                   // 찍은 판의 그림 차례(없으면 −1)
    private MeshStyle[] _fwStyle = Array.Empty<MeshStyle>();           // 차례마다 그리는 방식(고른 것이 바뀔 때 한 번 정해 둔다)
    private readonly TextBlock _fwPlateText = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
    private Button? _fwPlateButton;
    private Border? _fwPlateBar;

    /// <summary>[이전 단계](① 로 돌아간 뒤에 알린다) · [완료].</summary>
    public event Action? Back, Finish;

    /// <summary>대응표를 파일에 적는다("" = 됨, 아니면 까닭) — 종류를 바꿀 때마다 부른다(창을 그냥 닫아도 고친 것이 남게).</summary>
    public Func<string>? SaveRules { get; set; }

    /// <summary>② 의 상태(아직 ② 에 간 적이 없으면 null).</summary>
    public FormworkBoard? Board => _board;

    private int MemberCount => _memberMeshes.Count;

    /// <summary>② 거푸집면 설정으로 바꿔 놓는다. 3D 의 보는 자리는 그대로다. 같은 board 로 다시 오면 고른 줄도 그대로다.</summary>
    public void ShowFormwork(FormworkBoard board)
    {
        bool fresh = !ReferenceEquals(board, _board);
        _board = board;
        _fwSide ??= BuildFormworkSide();
        _fwTop ??= BuildFormworkTop();
        _fwBottom ??= BuildFormworkBottom();
        if (fresh)
        {
            _fwPicked = null;
            _fwPlate = null;
            BuildFormworkRows();
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
        // 줄 · 판을 고른 동안 옅게 그린 부재는 찍히지 않는다 — 그 뒤의 옅은 판을 찍어 바로 옮겨 고를 수 있게(부재를 찍어도 ② 에서는 할 일이 없다).
        _view3d.Pickable = i => i >= MemberCount || FormworkStyle(i) != MeshStyle.Tint;
        SetFormworkMeshes();
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ② 의 그릴 겉면 = 부재 + 덩이(판). 대응표를 고치거나 판을 빼면 덩이를 다시 지으므로 그때마다 갈아 끼운다(보는 자리는 그대로 —
    // 바뀌지 않은 판의 겉면은 그림이 다시 쓴다). 고른 것의 모습을 먼저 새 차례로 정한다(SetMeshes 가 바로 그린다).
    private void SetFormworkMeshes()
    {
        if (_board == null) return;
        var meshes = new List<MeshData>(_memberMeshes.Count + _board.Pieces.Count);
        meshes.AddRange(_memberMeshes);
        meshes.AddRange(_board.Pieces.Select(p => p.Mesh));
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
        _view3d.SetMeshes(_memberMeshes);
        ShowStep();
        RefreshAll();
        ShowMessage("");
    }

    // ── 3D ───────────────────────────────────────────────────────────────────────

    private FormworkPiece PieceAt(int index) => _board!.Pieces[index - MemberCount];

    // 부재와 그 부재의 덩이는 같은 거름(종류 · 층 · 숨긴 부재)을 탄다. '남은 것만 보기'는 보지 않는다(다 넣은 부재에도 거푸집이 있다).
    // 부재를 넘어 이은 판은 든 부재 하나라도 보이면 보인다.
    private bool FormworkShown(int index)
    {
        if (index < MemberCount) return _editor.InView(_members[index]);
        foreach (var m in PieceAt(index).Members)
            if (_editor.Find(m.Result.Element.Id) is not { } member || _editor.InView(member)) return true;
        return false;
    }

    // 부재 = 옅은 회색. 판 = 종류의 색(확인할 자리는 분홍) · 세지 않기로 한 덩이는 부재와 같은 회색 · 사람이 뺀 판은 옅은 회갈색(R48).
    private (byte r, byte g, byte b) FormworkColor(int index)
    {
        var grey = (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);
        if (index < MemberCount) return grey;
        var piece = PieceAt(index);
        if (piece.Dropped) return FormworkPalette.Dropped;
        var lead = piece.Lead;
        return lead.Counted || lead.NeedsCheck ? FormworkPalette.Of(lead) : grey;
    }

    // 흰 막 + 분홍 테두리: 찍은 판(R48) · [완료]의 산출식 확인 창이 보여 주는 부재. 고른 줄은 막 대신 그 줄의 판을 맨 위에 진하게 그린다(FormworkStyle).
    private bool FormworkPicked(int index) =>
        index < MemberCount ? _fwFocus is long focus && _members[index].Id == focus : index == _fwPickedIndex;

    // 그리는 방식(R48 — JACK 2026-10-16 «고른 줄만 진하게, 나머지 흐리게»): 고른 것이 없으면 모두 보통(면 + 모서리).
    // 줄이나 판을 고르면 부재는 옅게(뒤가 비친다) · 그 줄의 판(줄에 든 조각이 하나라도 있는 판 — 이은 판은 통째로)과 찍은 판은 맨 위에 제 색 ·
    // 다른 거푸집은 옅게.
    private MeshStyle FormworkStyle(int index) => index < _fwStyle.Length ? _fwStyle[index] : MeshStyle.Solid;

    // 고른 줄 · 찍은 판으로 그림 차례마다의 모습을 정해 둔다(그리기 · 찍기가 차례마다 묻는다 — 마우스가 움직일 때마다 줄을 다시 세지 않게).
    // 찍은 판은 대표 조각으로 다시 찾는다(덩이를 다시 지어 차례가 바뀌어도) — 없어졌으면 푼다.
    private void UpdateFormworkLook()
    {
        int count = MemberCount + (_board?.Pieces.Count ?? 0);
        _fwPickedIndex = _board != null && _fwPlate != null && _board.IndexOf(_fwPlate) is int at && at >= 0 ? MemberCount + at : -1;
        if (_fwPickedIndex < 0) _fwPlate = null;
        var styles = new MeshStyle[count];
        if (_board != null && (_fwPicked != null || _fwPickedIndex >= 0))
        {
            var inRow = _fwPicked is { } key ? _board.PiecesOf(key.type, key.group).ToHashSet() : new HashSet<int>();
            for (int i = 0; i < count; i++)
                styles[i] = i >= MemberCount && (inRow.Contains(i - MemberCount) || i == _fwPickedIndex) ? MeshStyle.Top : MeshStyle.Tint;
        }
        _fwStyle = styles;
    }

    private long? _fwFocus;     // [완료]의 산출식 확인 창이 보여 주는 부재(② 에서 — 흰 막으로 덮는다)

    /// <summary>
    /// 그 부재를 고른 채 그림 가운데에 보인다(산출식 확인 창이 부재를 하나씩 띄울 때). 0 = 풀기.
    /// ② 에서는 대응표의 줄 대신 그 부재를 덮고, ① 에서는 그 부재를 고른다(넣는 일은 하지 않는다).
    /// </summary>
    public void FocusMember(long id)
    {
        int index = IndexOf(id);
        if (Step == 1)
        {
            _fwFocus = id > 0 ? id : null;
            if (id > 0) { _fwPicked = null; _fwPlate = null; }
        }
        else _editor.Select(id > 0 && index >= 0 ? new[] { id } : Array.Empty<long>());
        RefreshAll();
        if (index >= 0) _view3d.ZoomTo(index);
    }

    // 판을 찍으면 그 판을 흰 막으로 덮고(그림 위 띠에 엑셀 줄 한 줄 + [이 판 빼기] · [되살리기]) 그 판을 정한 줄을 고른다
    // (대표 조각의 줄 — FormworkBoard.RowOf · 목록이 그 줄까지 굴러간다 · 뺀 판은 줄이 없다). 부재의 거푸집이 없는 면 · 빈 곳은 그대로 둔다(Esc 로 푼다).
    private void ClickFormwork(int index)
    {
        if (_board == null || index < MemberCount) return;
        var piece = PieceAt(index);
        _fwPlate = piece.Lead;
        _fwPicked = _board.RowOf(piece);
        RefreshAll();
        if (_fwPicked is { } key && _fwRowViews.TryGetValue(key, out var view)) view.row.BringIntoView();
    }

    // 표에서 줄을 골랐다(또는 Esc 로 풀었다): 찍은 판은 푼다.
    private void PickRow((string type, string group)? key)
    {
        _fwPicked = key;
        _fwPlate = null;
        RefreshAll();
    }

    /// <summary>마우스 밑의 것(② ): 판이면 엑셀 줄 한 줄(절 · 부위 · 부호 · 식 · 넓이), 부재면 거푸집이 없는 면.</summary>
    private string DescribeFormwork(int index)
    {
        if (_board == null) return "";
        if (index < MemberCount)
        {
            var m = _members[index];
            return $"{m.Category} {m.Type} · Id {m.Id} · 거푸집을 대지 않는 면(윗면 · 다른 콘크리트가 맞닿은 자리 등)";
        }
        return PieceText(PieceAt(index));
    }

    // 덩이를 한 줄로(마우스 밑 딱지 · 찍은 판의 띠): 판 = «{절} · {부위}{ - 작은 제목} · {부호} ({괄호}) : {식} · {넓이} ㎡ · 부재 n»(엑셀 줄과 같은 말 — R48) ·
    // 뺀 판 = «뺀 판 · {종류} · {넓이} ㎡ · 부재 n» · 판이 아닌 덩이(세지 않음 · 아직 정하지 않은 아래가 빈 밑면) = 유형 · 면 · 종류 · 넓이 · 까닭.
    private string PieceText(FormworkPiece piece)
    {
        int members = piece.Members.Select(m => m.Result.Element.Id).Distinct().Count();
        var lead = piece.Lead;
        if (piece.Dropped)
            return $"뺀 판 · {(lead.Item.Length > 0 ? lead.Item + " · " : "")}{M2(piece.Area)} ㎡ · 부재 {members}";
        if (piece.Plate is { } plate)
        {
            string title = plate.Part + (plate.Sub.Length > 0 ? " - " + plate.Sub : "");
            string tag = plate.Tag.Length > 0 ? $" ({plate.Tag})" : "";
            string check = lead.NeedsCheck ? " · 확인 필요" + (lead.Note.Length > 0 ? ": " + lead.Note : "") : "";
            return $"{plate.Section} · {title} · {plate.Mark}{tag} : {plate.Formula.Replace('*', '×')} · {M2(plate.Area)} ㎡ · 부재 {members}{check}";
        }
        var el = piece.Result.Element;
        string side = piece.Side.Length > 0 ? $"({piece.Side})" : "";
        string mark = el.Mark.Length > 0 ? $" {el.Mark}" : "";
        string part = el.Part.Length > 0 ? $" · 부위 {el.Part}" : "";
        return $"{el.Category} {el.Type}{mark} · Id {el.Id}{part} · {piece.Group}{side} · {KindText(lead)} · {M2(piece.Area)} ㎡" +
               (lead.Note.Length > 0 ? " · " + lead.Note : "");
    }

    // [이 판 빼기] · [되살리기](R48 — JACK 2026-10-16 «찍어서 빼기 · 판 한 장»): 대응표 파일에 적고(SaveRules) 덩이 · 줄 · 합계를 다시 센 뒤
    // 겉면을 갈아 끼운다(보는 자리는 그대로). 찍은 판은 대표 조각으로 다시 찾아 그대로 고른 채 둔다(뺀 뒤에는 회갈색 판 + [되살리기]).
    private bool ToggleDrop()
    {
        if (_board == null || Busy || _fwPlate is not { } lead) return false;
        int at = _board.IndexOf(lead);
        if (at < 0) return false;
        var piece = _board.Pieces[at];
        bool restoring = piece.Dropped;
        double area = piece.Area;
        var ids = piece.Members.Select(m => m.Result.Element.Id).Distinct().ToList();
        string text = PieceText(piece);
        if (!(restoring ? _board.Restore(piece) : _board.Drop(piece)))
        {
            // 열쇠가 판에 맞지 않아 빠지지 않았다(revit-verify R48 설치 전 L4) — 되돌려져 있으니 알리고 일지에만 남긴다.
            if (!restoring)
            {
                Log?.Invoke($"거푸집 판 뺄 수 없음\t{text} · 부재 Id {string.Join(",", ids.Take(8))}");
                ShowMessage("이 판은 뺄 수 없었습니다(판의 모양으로 자리를 찾지 못함) — 일지를 보내 주세요.", true);
            }
            return false;
        }
        string problem = SaveRules?.Invoke() ?? "";
        var watch = Stopwatch.StartNew();
        // 새 줄을 먼저 정한 뒤 그린다 — 첫 그림부터 새 줄로(revit-verify R48 설치 전 L2). 시간은 다 그린 뒤까지 잰다.
        int now = _board.IndexOf(lead);
        _fwPicked = now >= 0 ? _board.RowOf(_board.Pieces[now]) : null;
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke(FormattableString.Invariant(
            $"거푸집 판 {(restoring ? "되살림" : "뺌")}\t{text} · 부재 Id {string.Join(",", ids.Take(8))}{(ids.Count > 8 ? " …" : "")}") +
            $" · 뺀 판 {_board.DroppedCount}장({M2(_board.DroppedArea)} ㎡) · 모양이 바뀌어 다시 센 뺀 판 {_board.LostDrops.Count}" +
            $" · 일부만 찾음 {_board.DropCheck.Partial.Count} · 넓이 달라짐 {_board.DropCheck.Resized.Count} · 거푸집 {M2(_board.Total)} ㎡" +
            $" · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        if (problem.Length > 0)
            ShowMessage($"{(restoring ? "되살렸지만" : "뺐지만")} 대응표 파일에 적지 못했습니다(이 창에서는 바꾼 대로 셉니다): {problem}", true);
        else ShowMessage($"{(restoring ? "되살렸습니다" : "판을 뺐습니다")} · {M2(area)} ㎡ · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    private string KindText(FacePatch patch) =>
        patch.Counted ? $"{patch.Item} {(patch.Item == FormworkTable.Euro && _board != null ? FormworkTable.BandName(patch.Band, _board.Options.Bands) : "")}".TrimEnd()
        : patch.NeedsCheck ? "확인 필요" : FormworkRules.NotCounted;

    // ── 왼쪽 판: 대응표 ─────────────────────────────────────────────────────────

    private FrameworkElement BuildFormworkSide()
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var head = new TextBlock { Text = "거푸집 대응표", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 8) };
        _ui.Tip(head, "거푸집 종류를 고릅니다. 부위를 넣은 부재의 면은 맨 위 '부위' 줄에서, 그 밖(부위가 없는 부재 · 구멍 둘레 · 아래가 빈 밑면)은 유형 줄에서 정합니다 — " +
                      "한 면은 한 줄에만 있습니다. '초안대로'는 프로그램 규칙을 따릅니다. 줄을 누르면 그 면이 든 판이 3D 에 진하게 보입니다.");
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
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
        dock.Children.Add(new ScrollViewer { Content = _fwRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        return dock;
    }

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        return grid;
    }

    // 줄들: 유형마다 머리(카테고리 · 유형 · 부재 수) + 면의 갈래마다 한 줄([면][넓이][종류 ▾]). 줄의 차례는 대응표를 고쳐도 바뀌지 않는다.
    // 맨 위의 부위 줄(R47)은 머리가 «부위  이름  n개»(n = 그 부위를 넣은 부재 수)이고, 줄은 «모든 면(구멍 둘레 빼고)» 하나다.
    private void BuildFormworkRows()
    {
        _fwRows.Children.Clear();
        _fwRowViews.Clear();
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
            Grid.SetColumn(area, 1);
            Grid.SetColumn(combo, 2);
            grid.Children.Add(group);
            grid.Children.Add(area);
            grid.Children.Add(combo);
            var line = new Border { Child = grid, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 4, 3), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _ui.Mark(line, "", $"fw.row.{row.Type}.{row.Group}");
            line.MouseLeftButtonUp += (_, e) => { if (!Within(e.OriginalSource as DependencyObject, combo)) PickRow(key); };
            _fwRows.Children.Add(line);
            _fwRowViews[key] = (line, combo, area);
        }
    }

    // 종류 칸: [초안대로 · (초안의 종류)] · 유로폼 · 합판 3회 · 합판 4회 · 합판 6회 · 세지 않음. 높이 구간으로 자르지 않는 면(위를 보는 경사면 · 구멍 둘레)에는
    // 유로폼을 두지 않는다(R42 core 낮음). 파일에 적힌 낯선 값도 그대로 보인다.
    private ComboBox KindCombo(FormworkRow row, bool blinding)
    {
        string tip = FormworkRules.PartOf(row.Type) != null
            ? "이 부위의 모든 면(구멍 둘레 · 아래가 빈 밑면 빼고)에 적용됩니다."
            : "이 유형의 이 면에 적용됩니다(부위를 넣은 부재의 면은 부위 줄에서 정합니다).";
        if (blinding) tip += " 버림의 안쪽 면(호퍼 · 피트 둘레)은 초안에서 세지 않습니다 — 종류를 고르면 다시 셉니다.";
        var combo = _ui.Combo(tip, $"fw.kind.{row.Type}.{row.Group}");
        combo.MinHeight = 26;
        combo.FontSize = 12.5;
        combo.Padding = new Thickness(9, 0, 26, 1);
        combo.Items.Add(KindItem("", "초안대로 · " + row.Draft));
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
    private bool ChangeRule((string type, string group) key, string choice)
    {
        if (_board == null || Busy) return false;
        if (!_board.SetRule(key.type, key.group, choice)) return false;
        string problem = SaveRules?.Invoke() ?? "";
        _fwPicked = key;
        // 찍어 둔 판이 바꾼 줄의 판이 아니면 푼다 — 흰 막 · 띠가 다른 줄의 판에 남지 않게(revit-verify R48 설치 전 L3).
        if (_fwPlate is { } plate && (_board.IndexOf(plate) is var at && (at < 0 || !_board.InRow(_board.Pieces[at], key.type, key.group)))) _fwPlate = null;
        var watch = Stopwatch.StartNew();
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke($"거푸집 대응표\t{key.type} · {key.group} → {(choice.Length > 0 ? choice : "초안대로")} · 판 {_board.Pieces.Count(p => p.Plate != null)} · 거푸집 {M2(_board.Total)} ㎡" +
                    $" · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        string name = choice.Length > 0 ? choice : "초안대로";
        string row = FormworkRules.PartOf(key.type) is { } part ? $"부위 {part}" : $"{key.type} · {key.group}";
        if (problem.Length > 0)
            ShowMessage($"{row} → {name} — 대응표 파일에 적지 못했습니다(이 창에서는 바꾼 값으로 셉니다): {problem}", true);
        else ShowMessage($"{row} → {name} · 거푸집 {M2(_board.Total)} ㎡");
        return true;
    }

    // 모양이 바뀌어 찾지 못한 뺀 판의 기록을 지운다(revit-verify R48 설치 전 M1): 대응표 파일에 적고 화면을 맞춘다(ChangeRule 과 같은 길).
    private bool ForgetLostDrops()
    {
        if (_board == null || Busy) return false;
        var leads = _board.LostDrops.ToList();
        int n = _board.ForgetLostDrops();
        if (n == 0) return false;
        string problem = SaveRules?.Invoke() ?? "";
        var watch = Stopwatch.StartNew();
        SetFormworkMeshes();
        RefreshAll();
        Log?.Invoke($"찾지 못한 뺀 판 기록 지움\t{n}장 · " + string.Join(" / ", leads.Take(4)) + (leads.Count > 4 ? " …" : "") +
                    $" · 거푸집 {M2(_board.Total)} ㎡ · 판 다시 지음 {_board.RefreshMilliseconds} ms · 그림 {watch.ElapsedMilliseconds} ms");
        if (problem.Length > 0)
            ShowMessage($"찾지 못한 뺀 판 기록 {n}장을 지웠지만 대응표 파일에 적지 못했습니다: {problem}", true);
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
        _fwPlateButton = _ui.Button("이 판 빼기", "BtnSoft", "찍은 판을 거푸집에서 뺍니다 — 엑셀 · 거푸집 객체에 넣지 않습니다(회갈색으로 남아 [되살리기]로 되살립니다).", "btn.fw.drop");
        _fwPlateButton.Margin = new Thickness(10, 0, 0, 0);
        // 눌러도 글자판을 가져가지 않는다 — 가져가면 다음 Enter · Space 가 말없이 [되살리기]를 누른다(revit-verify R48 설치 전 L1).
        _fwPlateButton.Focusable = false;
        _fwPlateButton.Click += (_, _) => ToggleDrop();
        var bar = new DockPanel();
        DockPanel.SetDock(_fwPlateButton, Dock.Right);
        bar.Children.Add(_fwPlateButton);
        _ui.Mark(_fwPlateText, "", "fw.plate");
        bar.Children.Add(_fwPlateText);
        _fwPlateBar = new Border
        {
            Background = _ui["AccentSoft"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 6, 0, 0),
            Child = bar, Visibility = Visibility.Collapsed,
        };
        texts.Children.Add(_fwPlateBar);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = texts,
        };
    }

    // 그림 아래: 색 풀이 · 마우스 쓰는 법.
    private FrameworkElement BuildFormworkBottom()
    {
        var box = new DockPanel { Margin = new Thickness(2, 10, 2, 0) };
        var hint = new TextBlock
        {
            Text = "굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기 · 판을 찍으면 그 줄 · 빼기",
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
        Item(FormworkPalette.Dropped, "뺀 판");
        legend.Children.Add(new TextBlock { Text = "연한 색 = 안쪽 면 · 흰 막 = 찍은 판", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
        box.Children.Add(legend);
        return box;
    }

    // ② 의 화면을 지금 값으로 맞춘다: 줄의 바탕 · 넓이 · 초안 · 종류 칸 · 합계 · 아래 줄의 글.
    // 줄의 넓이와 초안은 바뀔 수 있다(판을 빼면 그 조각이 넓이에서 빠진다 · 붙은 벽의 종류가 바뀐다 — R47 · R48). 줄의 모임은 그대로다.
    private void RefreshFormwork()
    {
        if (_board == null) return;
        UpdateFormworkLook();
        _syncing = true;
        try
        {
            foreach (var row in _board.Rows)
            {
                if (!_fwRowViews.TryGetValue((row.Type, row.Group), out var view)) continue;
                bool picked = _fwPicked == (row.Type, row.Group);
                bool ask = row.Draft == FormworkSummary.Ask && row.Rule.Length == 0;   // 아직 정하지 않은 '아래가 빈 밑면'
                view.row.Background = picked ? _ui["AccentSoft"] : ask ? _ui["WarnBg"] : Brushes.Transparent;
                view.area.Text = M2(row.Area);
                if (view.combo.Items.Count > 0 && view.combo.Items[0] is ComboBoxItem { Tag: "" } draft && !Equals(draft.Content, "초안대로 · " + row.Draft))
                {
                    draft.Content = "초안대로 · " + row.Draft;
                    // 닫힌 칸은 고른 때의 글을 보인다 — 골라 둔 것이 초안이면 다시 골라 새 글이 보이게(_syncing 이라 종류는 바뀌지 않는다).
                    if (ReferenceEquals(view.combo.SelectedItem, draft)) { view.combo.SelectedItem = null; view.combo.SelectedItem = draft; }
                }
                SelectKind(view.combo, row.Rule);
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
        if (gaps > 0)
            _fwWarnings.Children.Add(WarnChip($"그림에 없는 틈 {gaps}곳",
                "틈 = 아래 콘크리트와 너무 가까워 거푸집을 댈 수 없는 밑면입니다(세지 않음 · 대응표 줄이 없어 3D 에 칠하지 않습니다).", "fw.warn.gaps"));
        if (splits > 0)
            _fwWarnings.Children.Add(WarnChip($"종류가 달라 못 이은 면 {splits}곳",
                "벽과 슬래브 옆처럼 같은 평면에 맞닿았지만 대응표의 종류가 달라 엑셀에서 한 줄로 잇지 못한 자리입니다. 종류를 맞추면 한 줄이 됩니다.\n"
                + string.Join("\n", _board.Merge.SplitSummary(5).Select(s => "  " + s)), "fw.warn.splits"));   // 풍선 글은 굴리지 않는다 — 앞 다섯 가지만(전부는 일지)
        // 사람이 뺀 판(R48): 합계 · 엑셀 · 거푸집 객체에서 빠진 것을 잊지 않게 · 모양이 바뀌어 다시 센 뺀 판(core · revit 계획 검토 M2).
        int dropped = _board.DroppedCount, lost = _board.LostDrops.Count;
        if (dropped > 0)
            _fwWarnings.Children.Add(WarnChip($"뺀 판 {dropped}장 · {M2(_board.DroppedArea)} ㎡",
                "3D 에서 뺀 판입니다 — 합계 · 엑셀 · 거푸집 객체에 넣지 않습니다(부재 속성(지침용) 합계에는 들어 있습니다). 회갈색 판을 찍고 [되살리기]로 되살립니다.", "fw.warn.dropped"));
        if (lost > 0)
        {
            // 기록을 지울 단추를 곁에 둔다(revit-verify R48 설치 전 M1 — 다시 찍어 빼도 옛 기록이 남아 경고가 영영 뜬다).
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(WarnChip($"모양이 바뀌어 다시 센 뺀 판 {lost}장",
                "전에 뺀 판의 자리를 이번 모양에서 찾지 못해 그 자리를 다시 셉니다(부재를 지웠거나 · 나중에 침 · 결합으로 면이 크게 바뀐 때). 필요하면 다시 찍어 빼세요.", "fw.warn.lost"));
            var forget = _ui.Button("기록 지우기", "BtnGhost", "찾지 못한 뺀 판의 기록을 대응표 파일에서 지웁니다(그 자리는 이미 다시 세고 있어 합계는 그대로입니다).", "btn.fw.forgetLost");
            forget.Focusable = false;
            forget.Margin = new Thickness(0, 4, 6, 0);
            forget.VerticalAlignment = VerticalAlignment.Center;
            forget.Click += (_, _) => ForgetLostDrops();
            line.Children.Add(forget);
            _fwWarnings.Children.Add(line);
        }
        // 찾았지만 모양이 바뀐 뺀 판(core-verify R48 설치 전 M3): 일부 조각만 찾음 · 빠진 넓이가 뺄 때와 5% 넘게 다름.
        var check = _board.DropCheck;
        if (check.Changed > 0)
            _fwWarnings.Children.Add(WarnChip($"모양이 바뀐 뺀 판 {check.Changed}장",
                (check.Partial.Count > 0 ? $"일부 조각만 찾은 판 {check.Partial.Count}장 — 찾은 조각만 빠지고 나머지는 다시 셉니다.\n" : "") +
                (check.Resized.Count > 0 ? $"빠진 넓이가 뺄 때와 다른 판 {check.Resized.Count}장 — 이웃 부재가 바뀌어 판이 커지거나 줄었습니다.\n" : "") +
                "회갈색 판을 찍어 넓이를 보고, 다르면 [되살리기] 뒤 다시 빼세요.", "fw.warn.changed"));
        // 부위 줄에 가려진 옛 유형 줄(core-verify R48 설치 전 M1): 부위를 넣은 부재의 면은 부위 줄만 정한다 — 전에 고친 유형 줄이 표에 없고 걸리지도 않는다.
        var hidden = _board.HiddenTypeRules;
        if (hidden.Count > 0)
            _fwWarnings.Children.Add(WarnChip($"부위 줄에 가려진 유형 줄 {hidden.Count}",
                "전에 고친 유형 줄인데, 그 면이 모두 부위를 넣은 부재라 부위 줄이 종류를 정합니다(이 줄은 걸리지 않음 · 파일에는 남음). 필요하면 부위 줄에서 고르세요.\n"
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
                       (dropped > 0 ? $" · 뺀 판 {dropped}장" : "") + (lost > 0 ? $" · 모양이 바뀌어 다시 센 뺀 판 {lost}장" : "") + "\n" + string.Join(" · ", parts);
        _status.Text = (_board.Rules.Count > 0 ? $"대응표에서 고친 줄 {_board.Rules.Count}" : "대응표: 초안 그대로") + (dropped > 0 ? $" · 뺀 판 {dropped}장" : "") + " · 고칠 때마다 파일에 적힙니다";

        // 찍은 판의 띠: 엑셀 줄 한 줄 + 판이면 [이 판 빼기], 뺀 판이면 [되살리기](판이 아닌 덩이 — 세지 않음 · 아직 정하지 않은 밑면 — 는 단추 없이 글만).
        if (_fwPlateBar != null && _fwPlateButton != null)
        {
            var piece = _fwPickedIndex >= 0 ? PieceAt(_fwPickedIndex) : null;
            _fwPlateBar.Visibility = piece == null ? Visibility.Collapsed : Visibility.Visible;
            if (piece != null)
            {
                _fwPlateText.Text = PieceText(piece);
                _fwPlateText.ToolTip = _fwPlateText.Text;
                _fwPlateButton.Content = piece.Dropped ? "되살리기" : "이 판 빼기";
                _fwPlateButton.Visibility = piece.Dropped || piece.Plate != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

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
    /// <summary>찍은 판의 띠 단추([이 판 빼기] · [되살리기])를 눌렀다(화면의 단추와 같은 길).</summary>
    public bool PressPlateButton() => ToggleDrop();
    /// <summary>찍은 판의 띠 단추(진짜 마우스로 누를 때 자리를 잰다).</summary>
    public Button? PlateButton => _fwPlateButton;
}
