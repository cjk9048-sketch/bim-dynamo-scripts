using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73a5 — [빼기] · [넣기] 도구(JACK 1102 «저런 부분 빼기 다시 넣기든 기능을 좀 개선해 줘. 위에서 고르는 것보다는 빼기 버튼을 누르고 직접 선택하는 방식으로 해 줘»):
//  · 3D 왼쪽 위 작은 칸 [빼기 | 넣기](켜고 끔 — 켠 것을 다시 누르거나 Esc = 내려놓음). 빗금이 있을 때만 켤 수 있다(이 부재만 보기 · 면 · 판을 고른 보통 보기 — 빗금을 그리는 그 면들).
//  · 든 동안: 마우스 = 십자 · 닿은 자리 위면 그 자리와 같은 기록이 바꾸는 자리(같은 부재 · 같은 평면 · 같은 이웃 — SamePlaneFaces)에 굵은 테두리 +
//    아래 이름표 «계단 · 0.03㎡ · 지금: 빠짐 → 넣기» · 자리 밖이면 «닿은 자리를 누르세요»(누르면 아무 일 없음 — 고른 것도 그대로).
//  · 누르면 그 (면 평면 · 이웃) 하나: [넣기] = «겹침: 무시» · [빼기] = «겹침: 공제»(SetFaceJoints — 형상부터 다시 잼) · 되돌리기 한 줄 · 빗금 · 딱지 · 카드가 따라 바뀜 · 도구는 든 채.
//    이미 그 상태면 짧은 알림 «이미 넣어져 있습니다» / «이미 빠져 있습니다»(기록 없음). 끌다 뗀 것은 받지 않는다(끌어 고르기는 든 동안 끔).
//  · 누른 자리 찾기 = Core FormworkBoard.CutAt: 시선이 빗금 면 평면을 지나는 점이 그 면의 빠진 · 넣은 고리 안인가(5 mm 여유) · 보이는 겉면(그 부재의 몸 · 판)보다 2 cm 넘게 뒤면 가려진 것.
//  · 수량은 누를 때만 바뀐다 · 모델 일은 FinishChange 의 기록 적기 길 그대로(창 처리기에서 Revit API 없음).
//  · R73a6(JACK 1102 «표현을 좀 전문적이게»): [공제] · [공제 제외] · 이름표 «계단 접합부 · 0.03㎡ · 현재: 공제 → 공제 제외» · 도구는 왼쪽 그림에서만(오른쪽 «전체 모델»은 고르기).
//  · R73a7(JACK 1102 «공제 제외를 누르면 그 버튼 아래로 합판부터 유로폼까지 버튼이 확장되고 거기서 누르고 위치를 누르면 해당 색으로 바뀌게»): [공제 제외]를 들면 아래 줄
//    [자동 | 유로폼 | 합판 3회 | 합판 4회 | 합판 6회] — 종류를 고르고 빗금을 누르면 공제 제외 + 그 자리가 든 판의 종류(SetKinds — 붙어 있던 판이면 그 판 전체 · JACK 정함) ·
//    되돌리기 한 줄. 이미 공제 제외된 자리(청록 점선)를 누르면 그 판의 종류만. 자동 = 판 종류는 그대로(규칙).
public sealed partial class PlacePartView
{
    private enum CutTool { None, Deduct, Keep }
    private CutTool _fwTool;
    private RadioButton? _fwToolDeduct, _fwToolKeep;
    private FrameworkElement? _fwTools;
    private FrameworkElement? _fwToolKinds;                        // [공제 제외] 아래 종류 줄(R73a7)
    private readonly Dictionary<string, RadioButton> _fwToolKindButtons = new(StringComparer.Ordinal);
    private string? _fwToolKind;                                    // 고른 종류(null = 자동 — 판 종류 그대로)
    private const string AutoKind = "자동";
    private bool _fwToolSync;                                       // 칸의 IsChecked 를 맞추는 중(Checked · Unchecked 가 다시 부르지 않게)
    private FormworkBoard.CutSpot? _fwToolSpot;                     // 마우스 밑 닿은 자리(든 동안)
    private string _fwToolKey = "";                                 // 굵은 테두리를 그린 빗금 열쇠(빗금이 바뀌면 걷는다)

    private const string ToolDeductTip = "공제 — 다른 부재와 닿는 접합부를 선택해 거푸집 면적에서 공제합니다";   // R73a6 전문 용어(JACK 1102 «빠진 → 공제»)
    private const string ToolKeepTip = "공제 제외 — 공제 위치(빨간 빗금)를 선택해 거푸집 면적에 포함합니다";

    private static string ToolName(CutTool tool) => tool == CutTool.Keep ? "공제 제외" : "공제";

    // 3D 왼쪽 위 [빼기 | 넣기](UiTheme 조각 — 켠 조각을 다시 누르면 내려놓는다: 조각은 스스로 꺼지지 않으므로 누르기 앞에서 받는다).
    private FrameworkElement BuildCutTools()
    {
        RadioButton Make(string name, string tip, string id, CutTool tool)
        {
            var segment = _ui.Segment(name, tip, id);
            segment.Focusable = false;
            segment.MinHeight = 28;
            segment.Padding = new Thickness(13, 0, 13, 1);
            segment.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (segment.IsChecked != true) return;
                e.Handled = true;
                SetTool(CutTool.None, "다시 누름");
            };
            segment.Checked += (_, _) => { if (!_fwToolSync) SetTool(tool, ""); };
            segment.Unchecked += (_, _) => { if (!_fwToolSync && _fwTool == tool) SetTool(CutTool.None, ""); };
            return segment;
        }
        _fwToolDeduct = Make("공제", ToolDeductTip, "fw.tool.deduct", CutTool.Deduct);
        _fwToolKeep = Make("공제 제외", ToolKeepTip, "fw.tool.keep", CutTool.Keep);
        var bar = _ui.Segments(_fwToolDeduct, _fwToolKeep);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        // [공제 제외] 아래 종류 줄(R73a7): 자동 + 판 종류(카드의 종류 단추와 같은 이름 · 3D 판 색 점).
        var kinds = new List<UIElement>();
        foreach (string item in new[] { AutoKind }.Concat(FormworkTable.Items.Where(i => i != FormworkRules.NotCounted)))
        {
            string choice = item;
            var segment = _ui.Segment(item, item == AutoKind ? "공제 제외한 자리는 붙어 있던 거푸집 판의 종류 그대로(규칙대로)"
                                                             : $"공제 제외한 자리가 든 거푸집 판을 «{item}»으로 합니다 — 붙어 있던 판이면 그 판 전체. 이미 공제 제외된 자리를 누르면 종류만 바꿉니다.", "fw.tool.kind." + item);
            segment.Focusable = false;
            segment.MinHeight = 26;
            segment.Padding = new Thickness(9, 0, 10, 1);
            segment.HorizontalAlignment = HorizontalAlignment.Stretch;
            segment.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (item != AutoKind)
            {
                var (r, g, b) = FormworkPalette.Base(item);
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = Swatch(r, g, b, 5);
                dot.Width = dot.Height = 9;
                content.Children.Add(dot);
                content.Children.Add(new TextBlock { Text = item, VerticalAlignment = VerticalAlignment.Center });
                segment.Content = content;
            }
            segment.IsChecked = item == AutoKind;
            segment.Checked += (_, _) =>
            {
                _fwToolKind = choice == AutoKind ? null : choice;
                Log?.Invoke($"② 공제 제외 종류\t{choice}");
                if (_fwTool != CutTool.None) HoverTool();
            };
            _fwToolKindButtons[item] = segment;
            kinds.Add(segment);
        }
        // 세로로 [공제 제외] 바로 아래(하네스 그림 — 가로 줄은 오른쪽 «부재 단독 보기» 띠에 가려 «합판 6회»를 누를 수 없었다 · JACK «그 버튼 아래로 … 확장»).
        var column = new StackPanel();
        foreach (var k in kinds) column.Children.Add(k);
        var kindRow = new Border { Background = _ui["Key"], CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = column };
        kindRow.Margin = new Thickness(0, 4, 0, 0);
        kindRow.HorizontalAlignment = HorizontalAlignment.Right;
        kindRow.MinWidth = 96;
        kindRow.Visibility = Visibility.Collapsed;
        _ui.Mark(kindRow, "공제 제외할 자리의 거푸집 종류 — 고른 뒤 빗금을 누르세요", "fw.tool.kinds");
        _fwToolKinds = kindRow;
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(kindRow);
        var chip = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = stack, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0),
        };
        _ui.Mark(chip, "", "fw.tools");
        _fwTools = chip;
        return chip;
    }

    // 도구를 든다 · 내려놓는다(why = 내려놓은 까닭 — 일지). 칸 · 마우스 모양 · 끌어 고르기 · 마우스 밑 표시를 맞춘다.
    private void SetTool(CutTool tool, string why)
    {
        if (tool != CutTool.None && !ToolsReady) tool = CutTool.None;
        var was = _fwTool;
        _fwTool = tool;
        if (was != tool)
        {
            if (was != CutTool.None) Log?.Invoke($"② {ToolName(was)} 도구 끔" + (why.Length > 0 ? "\t" + why : ""));
            if (tool != CutTool.None) Log?.Invoke($"② {ToolName(tool)} 도구 켬");
        }
        _fwToolSync = true;
        try
        {
            if (_fwToolDeduct != null) _fwToolDeduct.IsChecked = tool == CutTool.Deduct;
            if (_fwToolKeep != null) _fwToolKeep.IsChecked = tool == CutTool.Keep;
        }
        finally { _fwToolSync = false; }
        if (_fwToolKinds != null) _fwToolKinds.Visibility = tool == CutTool.Keep ? Visibility.Visible : Visibility.Collapsed;   // 종류 줄(R73a7)
        // 알림은 도구 칸 아래 왼쪽이라 펼친 종류 줄을 덮는다(하네스 그림) — 든 동안은 도구 칸 오른쪽으로.
        _toast.Margin = tool == CutTool.Keep ? new Thickness((_fwTools?.ActualWidth ?? 150) + 6, 0, 0, 0) : new Thickness(0);
        _view3d.ToolCursor = tool != CutTool.None ? Cursors.Cross : null;
        _view3d.BandSelect = Step == 0 || (Step == 1 && tool == CutTool.None);   // 든 동안은 끌어 고르지 않는다(끌다 뗀 것도 받지 않음)
        if (was == tool) return;
        _fwToolSpot = null;
        _view3d.SetToolRings(null);
        if (tool != CutTool.None) HoverTool();
        else if (Step == 1)
        {
            // 내려놓으면 전의 마우스 밑(면 테두리 · 이름표)으로 — 다음 움직임을 기다리지 않게.
            var at = _view3d.HoverPoint;
            var (index, triangle) = at is { } p ? _view3d.HitAt(p) : (-1, -1);
            HoverFormwork(index, triangle);
        }
    }

    // 도구를 쓸 수 있는가: ② · 다시 잴 수 있음 · 빗금(빠진 · 넣은 자리 고리)이 있음.
    private bool ToolsReady => Step == 1 && _board is { Remeasure: not null } && _fwCutList.Any(c => c.Rings.Count + c.KeptRings.Count > 0);

    // 칸을 지금 것으로(RefreshCutLines 끝에서): ② 에서만 보임 · 빗금이 없으면 흐리게 + 까닭 풍선 글 · 든 도구는 내려놓음 · 빗금이 바뀌면 굵은 테두리를 걷는다.
    private void RefreshCutTools()
    {
        if (_fwTools == null) return;
        _fwTools.Visibility = Step == 1 ? Visibility.Visible : Visibility.Collapsed;
        bool ready = ToolsReady;
        string off = _fwCutFaces == 0 ? "목록에서 부재를 선택하면 사용할 수 있습니다" : "선택한 부재에 다른 부재와의 접합부가 없습니다";
        foreach (var (segment, tip) in new[] { (_fwToolDeduct, ToolDeductTip), (_fwToolKeep, ToolKeepTip) })
        {
            if (segment == null) continue;
            segment.IsEnabled = ready;
            _ui.Tip(segment, ready ? tip : off);
        }
        if (!ready && _fwTool != CutTool.None) SetTool(CutTool.None, _fwCutFaces == 0 ? "선택 없음" : "접합부 없음");
        if (_fwCutKey != _fwToolKey)
        {
            _fwToolKey = _fwCutKey;
            _fwToolSpot = null;
            _view3d.SetToolRings(null);
        }
    }

    // 마우스 밑(든 동안): 닿은 자리면 굵은 테두리 + «계단 · 0.03㎡ · 지금: 빠짐 → 넣기» · 아니면 «닿은 자리를 누르세요» · 그림 밖이면 이름표를 접는다.
    private void HoverTool()
    {
        _fwHoverFace = null;
        _view3d.SetHoverOutline(null);
        if (_view3d.HoverPoint is not { } at || _board == null)
        {
            _fwToolSpot = null;
            _view3d.SetToolRings(null);
            _hover.Text = " ";
            _hoverPill.Visibility = Visibility.Collapsed;
            return;
        }
        var spot = ToolSpotAt(at);
        _fwToolSpot = spot;
        var origin = _board.Origin;
        _view3d.SetToolRings(spot?.Rings.Select(r => (IReadOnlyList<Vec3>)r.Select(p => p - origin).ToList()).ToList());
        // 까닭은 누르면 보일 때만(설치 전 검토 낮음 — ClickTool 과 같은 조건: 상부면이거나 지금 상태와 다른 도구).
        _hover.Text = spot == null ? "접합부(빗금)를 선택하세요"
                    : _board.ToolBlock(spot) is { } why && (spot.Face.Face.Class == FaceClass.Top || spot.Kept != (_fwTool == CutTool.Keep)) ? $"{spot.Label} 접합부 · {M2(spot.Area)}㎡ · {why}"
                    : ToolText(spot);
        _hoverPill.Visibility = Visibility.Visible;
    }

    // 이름표 글: «계단 · 0.03㎡ · 지금: 빠짐 → 넣기» · 이미 그 상태면 화살표 없이 «… · 지금: 넣음».
    // R73a7: 종류를 골랐으면 « → 공제 제외 · 합판 3회» · 이미 공제 제외된 자리면 « → 합판 3회»(종류만).
    private string ToolText(FormworkBoard.CutSpot spot)
    {
        bool keep = _fwTool == CutTool.Keep;
        string kind = keep && _fwToolKind != null ? _fwToolKind : "";
        return $"{spot.Label} 접합부 · {M2(spot.Area)}㎡ · 현재: {(spot.Kept ? "공제 제외" : "공제")}"
               + (spot.Kept != keep ? $" → {ToolName(_fwTool)}" + (kind.Length > 0 ? " · " + kind : "") : kind.Length > 0 ? " → " + kind : "");
    }

    // 화면의 그 자리의 닿은 자리(빗금을 건 면에서 · 보이는 겉면 = 그 면 주인 부재의 몸 · 그 부재가 든 판 · 고른 면 겉면까지).
    private FormworkBoard.CutSpot? ToolSpotAt(Point at)
    {
        if (_board == null || _fwCutFaceList.Count == 0) return null;
        var owners = _fwCutFaceList.Select(f => f.ElementId).ToHashSet();
        bool Own(int i) =>
            i < MemberCount ? owners.Contains(_members[i].Id)
            : i < OverlayStart ? _board.Pieces[i - MemberCount].Members.Any(m => owners.Contains(m.Result.Element.Id))
            : i >= FaceOverlayStart && i < ClipStart && owners.Contains(_fwFaceOverlay[i - FaceOverlayStart].face.ElementId);
        var (origin, direction) = _view3d.Camera.Ray(at.X, at.Y);
        double seen = _view3d.SurfaceDistance(at, Own) ?? double.PositiveInfinity;
        return _board.CutAt(_fwCutFaceList, origin + _board.Origin, direction, seen, FaceShown);   // 굵은 테두리는 보이는 면의 고리만(설치 전 검토 낮음)
    }

    // 3D 를 눌렀다(든 동안 — 고르기 대신): 닿은 자리면 넣기 · 빼기 · 아니면 아무 일 없음(이름표가 «닿은 자리를 누르세요»).
    private void ClickTool()
    {
        if (_board == null || Busy || _view3d.ClickMoved) return;
        var spot = ToolSpotAt(_view3d.ClickPoint);
        if (spot == null)
        {
            Log?.Invoke($"② {ToolName(_fwTool)} 도구 누름\t접합부 아님");
            return;
        }
        bool keep = _fwTool == CutTool.Keep;
        // R73a6 — 말없이 아무 일 없음이 되지 않게 · R73a7 상부면은 어느 도구든 까닭을 먼저.
        if (_board.ToolBlock(spot) is { } why && (spot.Face.Face.Class == FaceClass.Top || spot.Kept != keep)) { ShowMessage(why + "."); Log?.Invoke($"② {ToolName(_fwTool)} 도구 누름\t{why}"); return; }
        if (spot.Kept == keep)
        {
            if (keep && _fwToolKind != null) { ApplyToolKind(spot); return; }   // 이미 공제 제외된 자리 — 그 판의 종류만(R73a7)
            ShowMessage(keep ? "이미 공제 제외되어 있습니다." : "이미 공제되어 있습니다.");
            return;
        }
        ApplyTool(spot, keep);
    }

    // 누른 자리의 면 평면 위 점(모델 좌표 — 시선이 그 면 평면을 지나는 점 · CutAt 과 같은 셈) · 시선 방향. 평면과 나란하면 null.
    private (Vec3 point, Vec3 direction)? ToolPoint(FormworkBoard.CutSpot spot, Point at)
    {
        if (_board == null) return null;
        var (origin, direction) = _view3d.Camera.Ray(at.X, at.Y);
        if (direction.Length < 1e-12) return null;
        var dir = direction.Unit();
        var o = origin + _board.Origin;
        double dn = spot.Normal.Dot(dir);
        if (Math.Abs(dn) < 1e-9) return null;
        return (o + dir * ((spot.Face.Face.PlaneD - spot.Normal.Dot(o)) / dn), dir);
    }

    // 다시 잰 뒤 그 점이 든 판(그 부재 · 그 면 평면의 판 조각 — FaceAtPoint · 그 조각 테두리 안이어야: 고리로 찾은 같은 면의 다른 판이면 아님 — 설치 전 검토 낮음) ·
    // 산출 제외 · 산출 위치를 바꾼 판은 고르지 않는다(SetKinds 가 그 기록을 걷어 판 전체가 다시 세어진다 — 설치 전 검토 높음). (판 · 까닭) — 판이 없으면 까닭.
    private (FormworkPiece? piece, string why) PieceAt(long element, (Vec3 point, Vec3 direction)? at)
    {
        if (_board == null || at is not { } p) return (null, "그 자리를 찾지 못했습니다");
        if (_board.FaceAtPoint(element, p.point, p.direction) is not { Piece: >= 0 } face || face.Piece >= _board.Pieces.Count || face.Face.Normal is not { Length: 3 } nn
            || !face.Patches.Any(x => FormworkBoard.InsideRings(x.Outline, p.point, new Vec3(nn[0], nn[1], nn[2]))))
            return (null, "그 자리에 거푸집 판이 없습니다");
        var piece = _board.Pieces[face.Piece];
        if (piece.Dropped) return (null, "산출 제외된 판입니다 — 종류는 판 띠에서 바꾸세요");
        if (piece.Moved) return (null, "산출 위치를 바꾼 판입니다 — 종류는 판 띠에서 바꾸세요");
        return (piece, "");
    }

    // 이미 공제 제외된 자리(R73a7): 그 자리가 든 판의 종류만 고른 종류로(판 띠의 종류 단추와 같은 기록 — 붙어 있던 판이면 그 판 전체) · 되돌리기 한 줄.
    private bool ApplyToolKind(FormworkBoard.CutSpot spot)
    {
        if (_board == null || Busy || _fwToolKind is not { } kind) return false;
        string what = $"{spot.Label} 접합부 거푸집 종류 → {kind}";
        var (piece, why) = PieceAt(spot.Face.ElementId, ToolPoint(spot, _view3d.ClickPoint));
        if (piece == null)
        {
            ShowMessage($"종류를 바꾸지 못했습니다 — {why}.");
            Log?.Invoke($"② 공제 제외 종류\t{what} · {why}");
            return false;
        }
        string snapshot = _board.Snapshot();
        int n = _board.SetKinds(new[] { piece }, kind);
        if (n == 0) { RefreshAll(); ShowMessage($"이미 {kind}입니다."); return false; }
        RecordUndo(what, snapshot);
        FinishChange($"② 공제 제외 종류\t부재 {spot.Face.ElementId} · {spot.Face.Name} · {spot.Label}(Id {spot.Other}) · 판 {piece.Area:0.##}㎡ → {kind}", $"{what} · 판 {M2(piece.Area)} ㎡", what);
        _fwToolSpot = null;
        if (_fwTool != CutTool.None) HoverTool();
        return true;
    }

    // 그 (면 평면 · 이웃) 하나를 넣기 · 빼기(되돌리기 한 줄 · 형상부터 다시 잼 · 기록을 모델에) · 다시 잰 뒤 마우스 밑을 새 면으로.
    // R73a7: 넣기에 종류를 골랐으면 다시 잰 뒤 누른 자리가 든 판의 종류도(같은 되돌리기 한 줄).
    private bool ApplyTool(FormworkBoard.CutSpot spot, bool keep)
    {
        if (_board == null || Busy) return false;
        string name = keep ? "공제 제외" : "공제", what = $"{spot.Label} 접합부 {name}";
        string? kind = keep ? _fwToolKind : null;
        var at = kind != null ? ToolPoint(spot, _view3d.ClickPoint) : null;   // 다시 재기 전에(카메라 그대로 — 면 평면 위 점)
        // 바뀌는 면(그 이웃이 지금 상태로 걸린 같은 평면 면) — 다시 재면 면이 새것이라 먼저 센다.
        int changed = spot.Faces.Select(f => f.Face).Distinct().Count(face => (keep ? face.Covers.Concat(face.Cut) : face.Ignored).Any(c => c.ElementId == spot.Other));
        string head = $"② {name}\t부재 {spot.Face.ElementId} · {spot.Face.Name} · {spot.Label}(Id {spot.Other}) · {M2(spot.Area)}㎡ · 바뀐 면 {changed}";
        string snapshot = _board.Snapshot();
        double before = _board.Total;
        int n = _board.SetFaceJoints(spot.Faces, new[] { spot.Other }, keep);
        if (n == 0)
        {
            Log?.Invoke(head + " · 바뀐 것 없음");
            RefreshAll();
            ShowMessage("바뀐 것이 없습니다.");
            return false;
        }
        double delta = _board.Total - before;   // R73a6: 바뀐 넓이를 알림에(드러난 자리 일부가 같은 평면의 다른 부재 면과 포개지면 빗금보다 적게 바뀐다)
        if (Math.Abs(delta) < 1e-6)
        {
            // 설치 전 검토 중간 — 안전망: 기록은 걸렸는데 거푸집이 그대로(받친 하부면 등) → 기록을 되돌리고(모델에 적지 않음 · 되돌리기 줄 없음) 짧게 알린다.
            _board.RestoreSnapshot(snapshot);
            bool resting = spot.Face.Face.Class is FaceClass.Bottom or FaceClass.SlopeDown;
            string why = resting ? "변화 없음 — 받친 하부면 접합부입니다" : "변화 없음 — 거푸집 수량이 바뀌지 않는 자리입니다";
            Log?.Invoke(head + " · " + why + " · 기록 되돌림");
            SetFormworkMeshes();
            RefreshAll();
            ShowMessage(why + ".");
            _fwToolSpot = null;
            if (_fwTool != CutTool.None) HoverTool();
            return false;
        }
        string kindNote = "", kindSaid = "";
        if (kind != null)
        {
            // 다시 잰 판에서 누른 자리가 든 판 — 종류 기록(SetKinds 도 형상은 그대로 · 판만 다시 짓는다). 판이 없으면(확인할 자리 등) · 산출 제외 · 위치를 바꾼 판이면 공제 제외만
            // (설치 전 검토: 알림에도 그대로 — 종류를 못 바꿨으면 그 까닭).
            var (piece, why) = PieceAt(spot.Face.ElementId, at);
            if (piece == null) (kindNote, kindSaid) = ($" · 종류 못 바꿈({why})", $" · 종류는 그대로({why})");
            else if (_board.SetKinds(new[] { piece }, kind) > 0) (kindNote, kindSaid) = ($" · 판 {M2(piece.Area)}㎡ → {kind}", $" · 판 {M2(piece.Area)}㎡ → {kind}");
            else (kindNote, kindSaid) = ($" · {kind} 이미 같음", $" · 이미 {kind}");
            if (piece != null) what += " · " + kind;
        }
        delta = _board.Total - before;             // 종류까지 건 뒤(설치 전 검토 높음 — 알림의 넓이 변화가 실제 합계 변화와 같게)
        RecordUndo(what, snapshot);
        FinishChange(head + $" · 합계 {(delta >= 0 ? "+" : "−")}{M2(Math.Abs(delta))}㎡" + kindNote, $"{spot.Label} 접합부 {name}{kindSaid} · 거푸집 {(delta >= 0 ? "+" : "−")}{M2(Math.Abs(delta))} ㎡ → {M2(_board.Total)} ㎡", what);
        _fwToolSpot = null;
        if (_fwTool != CutTool.None) HoverTool();
        return true;
    }

    // ── 시험에서 쓰는 길 ──

    /// <summary>R73a5: 3D 왼쪽 위 도구 칸 · 그 조각(«deduct» · «keep») · 지금 든 도구(«» · «빼기» · «넣기») · 마우스 밑 닿은 자리.</summary>
    public FrameworkElement? CutToolsBar => _fwTools;
    public RadioButton? CutToolButton(bool keep) => keep ? _fwToolKeep : _fwToolDeduct;

    /// <summary>R73a7: [공제 제외] 아래 종류 줄 · 그 종류 조각(«자동» · 판 종류) · 고른 종류(«» = 자동).</summary>
    public FrameworkElement? CutToolKinds => _fwToolKinds;
    public RadioButton? CutToolKindButton(string item) => _fwToolKindButtons.TryGetValue(item, out var b) ? b : null;
    public string CutToolKind => _fwToolKind ?? "";
    public string HeldTool => _fwTool == CutTool.None ? "" : ToolName(_fwTool);
    public FormworkBoard.CutSpot? ToolSpot => _fwToolSpot;

    /// <summary>R73a5: 화면의 그 자리에 도구가 바꿀 닿은 자리(누르기와 같은 찾기 — 없으면 null).</summary>
    public FormworkBoard.CutSpot? ToolSpotAtPoint(Point at) => ToolSpotAt(at);
}
