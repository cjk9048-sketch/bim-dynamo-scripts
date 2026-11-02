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
public sealed partial class PlacePartView
{
    private enum CutTool { None, Deduct, Keep }
    private CutTool _fwTool;
    private RadioButton? _fwToolDeduct, _fwToolKeep;
    private FrameworkElement? _fwTools;
    private bool _fwToolSync;                                       // 칸의 IsChecked 를 맞추는 중(Checked · Unchecked 가 다시 부르지 않게)
    private FormworkBoard.CutSpot? _fwToolSpot;                     // 마우스 밑 닿은 자리(든 동안)
    private string _fwToolKey = "";                                 // 굵은 테두리를 그린 빗금 열쇠(빗금이 바뀌면 걷는다)

    private const string ToolDeductTip = "빼기 — 다른 부재가 닿은 자리를 눌러 거푸집에서 뺍니다";
    private const string ToolKeepTip = "넣기 — 빠진 자리(빨간 빗금)를 눌러 거푸집으로 셉니다";

    private static string ToolName(CutTool tool) => tool == CutTool.Keep ? "넣기" : "빼기";

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
        _fwToolDeduct = Make("빼기", ToolDeductTip, "fw.tool.deduct", CutTool.Deduct);
        _fwToolKeep = Make("넣기", ToolKeepTip, "fw.tool.keep", CutTool.Keep);
        var bar = _ui.Segments(_fwToolDeduct, _fwToolKeep);
        var chip = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = bar, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0),
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
        string off = _fwCutFaces == 0 ? "목록에서 부재를 고르면 쓸 수 있습니다" : "고른 것에 다른 부재가 닿은 자리가 없습니다";
        foreach (var (segment, tip) in new[] { (_fwToolDeduct, ToolDeductTip), (_fwToolKeep, ToolKeepTip) })
        {
            if (segment == null) continue;
            segment.IsEnabled = ready;
            _ui.Tip(segment, ready ? tip : off);
        }
        if (!ready && _fwTool != CutTool.None) SetTool(CutTool.None, _fwCutFaces == 0 ? "고른 것 없음" : "닿은 자리 없음");
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
        _hover.Text = spot == null ? "닿은 자리를 누르세요" : ToolText(spot);
        _hoverPill.Visibility = Visibility.Visible;
    }

    // 이름표 글: «계단 · 0.03㎡ · 지금: 빠짐 → 넣기» · 이미 그 상태면 화살표 없이 «… · 지금: 넣음».
    private string ToolText(FormworkBoard.CutSpot spot)
    {
        bool keep = _fwTool == CutTool.Keep;
        return $"{spot.Label} · {M2(spot.Area)}㎡ · 지금: {(spot.Kept ? "넣음" : "빠짐")}" + (spot.Kept != keep ? $" → {ToolName(_fwTool)}" : "");
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
        return _board.CutAt(_fwCutFaceList, origin + _board.Origin, direction, seen);
    }

    // 3D 를 눌렀다(든 동안 — 고르기 대신): 닿은 자리면 넣기 · 빼기 · 아니면 아무 일 없음(이름표가 «닿은 자리를 누르세요»).
    private void ClickTool()
    {
        if (_board == null || Busy || _view3d.ClickMoved) return;
        var spot = ToolSpotAt(_view3d.ClickPoint);
        if (spot == null)
        {
            Log?.Invoke($"② {ToolName(_fwTool)} 도구 누름\t닿은 자리 아님");
            return;
        }
        bool keep = _fwTool == CutTool.Keep;
        if (spot.Kept == keep) { ShowMessage(keep ? "이미 넣어져 있습니다." : "이미 빠져 있습니다."); return; }
        ApplyTool(spot, keep);
    }

    // 그 (면 평면 · 이웃) 하나를 넣기 · 빼기(되돌리기 한 줄 · 형상부터 다시 잼 · 기록을 모델에) · 다시 잰 뒤 마우스 밑을 새 면으로.
    private bool ApplyTool(FormworkBoard.CutSpot spot, bool keep)
    {
        if (_board == null || Busy) return false;
        string name = keep ? "넣기" : "빼기", what = $"{spot.Label} {name}";
        // 바뀌는 면(그 이웃이 지금 상태로 걸린 같은 평면 면) — 다시 재면 면이 새것이라 먼저 센다.
        int changed = spot.Faces.Select(f => f.Face).Distinct().Count(face => (keep ? face.Covers.Concat(face.Cut) : face.Ignored).Any(c => c.ElementId == spot.Other));
        string head = $"② {name}\t부재 {spot.Face.ElementId} · {spot.Face.Name} · {spot.Label}(Id {spot.Other}) · {M2(spot.Area)}㎡ · 바뀐 면 {changed}";
        string snapshot = _board.Snapshot();
        int n = _board.SetFaceJoints(spot.Faces, new[] { spot.Other }, keep);
        if (n == 0)
        {
            Log?.Invoke(head + " · 바뀐 것 없음");
            RefreshAll();
            ShowMessage("바뀐 것이 없습니다.");
            return false;
        }
        RecordUndo(what, snapshot);
        FinishChange(head, $"{what} · 거푸집 {M2(_board.Total)} ㎡", what);
        _fwToolSpot = null;
        if (_fwTool != CutTool.None) HoverTool();
        return true;
    }

    // ── 시험에서 쓰는 길 ──

    /// <summary>R73a5: 3D 왼쪽 위 도구 칸 · 그 조각(«deduct» · «keep») · 지금 든 도구(«» · «빼기» · «넣기») · 마우스 밑 닿은 자리.</summary>
    public FrameworkElement? CutToolsBar => _fwTools;
    public RadioButton? CutToolButton(bool keep) => keep ? _fwToolKeep : _fwToolDeduct;
    public string HeldTool => _fwTool == CutTool.None ? "" : ToolName(_fwTool);
    public FormworkBoard.CutSpot? ToolSpot => _fwToolSpot;

    /// <summary>R73a5: 화면의 그 자리에 도구가 바꿀 닿은 자리(누르기와 같은 찾기 — 없으면 null).</summary>
    public FormworkBoard.CutSpot? ToolSpotAtPoint(Point at) => ToolSpotAt(at);
}
