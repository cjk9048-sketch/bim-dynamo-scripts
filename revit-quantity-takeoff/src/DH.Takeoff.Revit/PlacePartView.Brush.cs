using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73b — 붓(JACK 1030 «초보도 원하는 위치를 쉽게 선택해서 거푸집으로 설정 … 쉽게 수정» · 1103 «R73b 진행해 · 계단 옆면처럼 쪼개지지 않게» · 계획 §3 · §10.2):
//  · 3D 왼쪽 위 도구 칸 셋째 조각 [칠하기] — 들면 아래 세로 종류 줄(유로폼 · 합판 3회 · 4회 · 6회 · 산출 제외 · 처음 = 유로폼 · [공제 제외] 아래 줄과 같은 꼴).
//  · 든 동안: 마우스 밑 면(한 평면의 무리) 굵은 테두리 + 이름표 «부재 · 면 · ㎡ · 현재: 합판 3회 → 유로폼» · 누르면 칠함 · 끌면 지나간 면을 미리보기 색 → 놓을 때 한 번
//    (되돌리기 한 줄 «칠하기 유로폼 · 면 n» · 모델에 한 번). 왼쪽(부재 단독) · 보통 보기에서 · 오른쪽 «전체 모델»은 고르기만(R73a6).
//  · 나무 줄 끝 종류 글 = 종류 다섯 작은 판 → 고르면 그 면 칠함 · 줄 이름은 고르기 · 날기만(JACK 1030 정함).
//  · 판 띠 [↺ 처음 값] = 고른 면만(칠한 종류 · 산출 제외 · 판 종류 몫을 걷음).
//  · 쉬는 기록(모드 «자동» + 기록 — [자동 | 사용자] 단추를 걷었다)이면 첫 고침에 세 갈래 창 [다시 쓰고 계속] · [지우고 계속] · [취소] — 고침과 함께 되돌리기 한 줄 · 저장 한 번.
//  · 창 처리기에서 Revit API 없음 — 기록은 FinishChange 의 SaveRules 길 그대로.
public sealed partial class PlacePartView
{
    private RadioButton? _fwToolPaint;
    private FrameworkElement? _fwPaintKinds;
    private readonly Dictionary<string, RadioButton> _fwPaintKindButtons = new(StringComparer.Ordinal);
    private string _fwPaintKind = FormworkTable.Euro;                // 고른 붓 종류(기록 값 — 산출 제외 = «세지 않음»)
    private BoardFace? _fwPaintSpot;                                 // 마우스 밑 면(든 동안)
    private readonly List<BoardFace> _fwPaintDrag = new();           // 끌어 지나간 면(무리마다 하나)
    private Button? _fwFaceReset;                                    // 판 띠 [↺ 처음 값](고른 면만)

    private const string PaintTip = "칠하기 — 종류를 고르고 면을 클릭하거나 끌어서 칠합니다(Esc = 내려놓기)";

    // 붓 종류의 보이는 말(기록 값 «세지 않음» = 산출 제외).
    private static string PaintName(string item) => item == FormworkRules.NotCounted ? DropLabel : item;

    private static (byte r, byte g, byte b) PaintColor(string item) => item == FormworkRules.NotCounted ? FormworkPalette.Dropped : FormworkPalette.Base(item);

    // 붓을 들 수 있는가: ② · 판이 있음(빗금과 상관없이).
    private bool PaintReady => Step == 1 && _board != null;

    // [칠하기] 아래 세로 종류 줄(R73a7 [공제 제외] 아래 줄과 같은 꼴 · 판 색 점).
    private FrameworkElement BuildPaintKinds()
    {
        var column = new StackPanel();
        foreach (string item in FormworkTable.Items)
        {
            string choice = item;
            var segment = _ui.Segment(PaintName(item), item == FormworkRules.NotCounted ? "칠한 면을 산출에서 제외합니다" : $"칠한 면을 «{item}»으로 합니다", "fw.paint.kind." + item);
            segment.Focusable = false;
            segment.MinHeight = 26;
            segment.Padding = new Thickness(9, 0, 10, 1);
            segment.HorizontalAlignment = HorizontalAlignment.Stretch;
            segment.HorizontalContentAlignment = HorizontalAlignment.Left;
            var (r, g, b) = PaintColor(item);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = Swatch(r, g, b, 5);
            dot.Width = dot.Height = 9;
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = PaintName(item), VerticalAlignment = VerticalAlignment.Center });
            segment.Content = content;
            segment.IsChecked = item == _fwPaintKind;
            segment.Checked += (_, _) =>
            {
                _fwPaintKind = choice;
                Log?.Invoke($"② 칠하기 종류\t{PaintName(choice)}");
                if (_fwTool == CutTool.Paint) HoverTool();
            };
            _fwPaintKindButtons[item] = segment;
            column.Children.Add(segment);
        }
        var box = new Border
        {
            Background = _ui["Key"], CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = column,
            Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 96, Visibility = Visibility.Collapsed,
        };
        _ui.Mark(box, "칠할 거푸집 종류 — 고른 뒤 면을 클릭하거나 끌어서 칠하세요", "fw.paint.kinds");
        _fwPaintKinds = box;
        return box;
    }

    // 칠할 수 없는 까닭(마우스 밑 · 누름 · 나무 — 형상이 세지 않는 면).
    private static string PaintBlock(BoardFace face)
    {
        if (face.Siblings.All(f => f.Covered)) return "전부 공제된 면";
        var patches = face.Siblings.Where(f => !f.Covered).SelectMany(f => f.Patches).ToList();
        if (patches.Any(p => p.Below == Below.Gap)) return "하부 콘크리트와 너무 가까운 하부면";
        if (patches.Any(p => p.Below == Below.Ground)) return "지면 위 하부면";
        if (face.Face.Class == FaceClass.Top) return "상부면";
        return "거푸집 산출 대상이 아닌 면";
    }

    // 붓 이름표: «부재 · 면 · ㎡ · 현재: 합판 3회 → 유로폼»(이미 그 종류면 화살표 없이).
    private string PaintText(BoardFace face)
    {
        string now = FaceKind(face), next = PaintName(_fwPaintKind);
        return FaceText(face) + (now == next ? "" : " → " + next);
    }

    // 마우스 밑(붓을 든 동안): 면 굵은 테두리 + 이름표 · 칠할 수 없으면 까닭.
    private void HoverPaint()
    {
        _view3d.SetToolRings(null);
        if (_board == null || _view3d.HoverPoint is not { } at)
        {
            _fwPaintSpot = null;
            _fwHoverFace = null;
            _view3d.SetHoverOutline(null);
            _hover.Text = " ";
            _hoverPill.Visibility = Visibility.Collapsed;
            return;
        }
        var (index, triangle) = _view3d.HitAt(at);
        var face = HitFace(index, triangle, at);
        _fwPaintSpot = face;
        if (!ReferenceEquals(face, _fwHoverFace))
        {
            _fwHoverFace = face;
            _view3d.SetHoverOutline(face != null ? FaceMesh(face) : null);
        }
        _hover.Text = face == null ? (index >= 0 ? "거푸집 면이 아닙니다(상부면 · 접합부 등)" : "칠할 면을 선택하세요")
                    : !FormworkBoard.Paintable(face) ? $"{MemberLabel(face.Result.Element)} · {face.Name} · 칠할 수 없음({PaintBlock(face)})"
                    : PaintText(face);
        _hoverPill.Visibility = Visibility.Visible;
    }

    // 3D 를 눌렀다(붓을 든 동안 — 고르기 대신): 그 면을 칠함 · 칠할 수 없으면 까닭 한 줄.
    private void ClickPaint()
    {
        if (_board == null || Busy || _view3d.ClickMoved) return;
        var at = _view3d.ClickPoint;
        var (index, triangle) = _view3d.HitAt(at);
        var face = HitFace(index, triangle, at);
        if (face == null) { Log?.Invoke("② 칠하기 누름\t면 아님"); return; }
        if (!FormworkBoard.Paintable(face))
        {
            ShowMessage($"칠할 수 없는 면입니다 — {PaintBlock(face)}.");
            Log?.Invoke($"② 칠하기 누름\t부재 {face.ElementId} · {face.Name} · {PaintBlock(face)}");
            return;
        }
        PaintFaces(new[] { face }, _fwPaintKind, "누름");
    }

    // 붓 끌기(Model3DView.PaintMoved): 지나간 칠할 수 있는 면을 모아 미리보기 색으로(다시 세지 않음).
    private void PaintMovedTo(Point at)
    {
        if (_board == null || _fwTool != CutTool.Paint) return;
        var (index, triangle) = _view3d.HitAt(at);
        var face = HitFace(index, triangle, at);
        if (face != null && FormworkBoard.Paintable(face) && !_fwPaintDrag.Any(f => ReferenceEquals(f.Siblings[0], face.Siblings[0])))
        {
            _fwPaintDrag.Add(face);
            var origin = _board.Origin;
            var rings = new List<IReadOnlyList<Vec3>>();
            foreach (var f in _fwPaintDrag.SelectMany(x => x.Siblings).Where(x => !x.Covered).Distinct())
            {
                var outline = f.Face.Normal is { Length: 3 } n ? FormworkBoard.UnionOutline(f.Patches, n) : f.Patches.SelectMany(p => p.Outline).ToList();
                rings.AddRange(outline.Where(r => r.Count >= 3).Select(r => (IReadOnlyList<Vec3>)r.Select(q => new Vec3(q[0], q[1], q[2]) - origin).ToList()));
            }
            _view3d.SetPaintPreview(rings, PaintColor(_fwPaintKind));
        }
        if (!ReferenceEquals(face, _fwHoverFace))
        {
            _fwHoverFace = face;
            _view3d.SetHoverOutline(face != null ? FaceMesh(face) : null);
        }
        _hover.Text = $"칠하는 중 · 면 {_fwPaintDrag.Count} → {PaintName(_fwPaintKind)}";
        _hoverPill.Visibility = Visibility.Visible;
    }

    // 붓 끌기가 끝났다: 뗐으면 지나간 면을 한 번에(되돌리기 한 줄 · 저장 한 번) · 그만뒀으면 미리보기만 걷는다.
    private void PaintDragEnded(bool commit)
    {
        var faces = _fwPaintDrag.ToList();
        EndPaintDrag();
        if (!commit || faces.Count == 0)
        {
            if (faces.Count > 0) Log?.Invoke($"② 칠하기 끌기 그만둠\t면 {faces.Count}");
            return;
        }
        PaintFaces(faces, _fwPaintKind, "끌기");
    }

    private void EndPaintDrag()
    {
        _fwPaintDrag.Clear();
        _view3d.SetPaintPreview(null, (0, 0, 0));
    }

    // 면들을 칠한다(누름 · 끌기 · 나무): 기록 글을 떠 두고 · 칠하고 · 되돌리기 한 줄 · 모델에 한 번 · 다시 그린다. 쉬는 기록이면 세 갈래 창.
    private bool PaintFaces(IReadOnlyList<BoardFace> faces, string kind, string how)
    {
        if (_board == null || Busy || faces.Count == 0) return false;
        var groups = faces.Select(f => f.Siblings[0]).Distinct().ToList();
        string name = PaintName(kind);
        string labels = string.Join(", ", groups.Take(4).Select(f => $"{f.ElementId} {f.Name}")) + (groups.Count > 4 ? " …" : "");
        string snapshot = _board.Snapshot();
        double before = _board.Total;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var o = _board.Paint(groups, kind);
        long ms = watch.ElapsedMilliseconds;
        if (o.NeedsDecision)
        {
            AskWake($"칠하기 {name} · 면 {groups.Count}", () => PaintFaces(groups, kind, how));
            return false;
        }
        if (o.Painted == 0)
        {
            Log?.Invoke($"② 칠하기\t{how} · {name} · 면 {groups.Count}({labels}) · 바뀐 것 없음 · 같음 {o.Same} · 칠할 수 없음 {o.Skipped}");
            RefreshAll();
            ShowMessage(o.Same == 0 ? (groups.Count == 1 ? $"칠할 수 없는 면입니다 — {PaintBlock(groups[0])}." : $"칠할 수 없는 면입니다(면 {o.Skipped}).") : $"이미 {name}입니다.");
            return false;
        }
        string what = $"칠하기 {name} · 면 {o.Painted}";
        RecordUndo(what, snapshot);
        double delta = _board.Total - before;
        string rest = (o.Skipped > 0 ? $" · {o.Skipped} 칠할 수 없음" : "") + (o.Same > 0 ? $" · {o.Same} 이미 {name}" : "");
        FinishChange($"② 칠하기\t{how} · {name} · 면 {o.Painted}/{groups.Count}({labels}) · 같음 {o.Same} · 칠할 수 없음 {o.Skipped} · 합계 {(delta >= 0 ? "+" : "−")}{M2(Math.Abs(delta))}㎡" +
                     $" · 면 종류 기록 {_board.Rules.FaceKindCount} · 칠하기 {ms} ms(다시 셈 {_board.RefreshMilliseconds} ms)",
            $"면 {o.Painted} 칠함 → {name}{rest} · 거푸집 {M2(_board.Total)} ㎡", what);
        _fwPaintSpot = null;
        if (_fwTool != CutTool.None) HoverTool();
        return true;
    }

    // 판 띠 [↺ 처음 값](고른 면만): 그 면의 칠한 종류 · 산출 제외 · 판 종류 · 옮김 몫을 걷는다.
    private bool ResetSelectedFaces()
    {
        if (_board == null || Busy) return false;
        var groups = FocusFaces().Where(f => f.Siblings.Any(s => s.Painted)).Select(f => f.Siblings[0]).Distinct().ToList();
        if (groups.Count == 0) { ShowMessage("직접 정한 것이 없는 면입니다."); return false; }
        string what = $"면 처음 값 · 면 {groups.Count}";
        string snapshot = _board.Snapshot();
        double before = _board.Total;
        int n = _board.ResetFaces(groups);
        if (n == 0)
        {
            if (_board.NeedsDecision) { AskWake(what, () => ResetSelectedFaces()); return false; }
            RefreshAll();
            ShowMessage("직접 정한 것이 없는 면입니다.");
            return false;
        }
        RecordUndo(what, snapshot);
        double delta = _board.Total - before;
        FinishChange($"② 면 처음 값\t면 {n}({string.Join(", ", groups.Take(4).Select(f => $"{f.ElementId} {f.Name}"))}) · 합계 {(delta >= 0 ? "+" : "−")}{M2(Math.Abs(delta))}㎡",
            $"면 {n} → 처음 값 · 거푸집 {M2(_board.Total)} ㎡", what);
        return true;
    }

    // 판 띠의 [↺ 처음 값]: 고른 면에 걷을 기록이 있을 때만 · 풍선 글에 처음 값.
    private void RefreshFaceReset()
    {
        if (_fwFaceReset == null) return;
        var faces = FocusFaces().Where(f => f.Siblings.Any(s => s.Painted)).ToList();
        _fwFaceReset.Visibility = faces.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (faces.Count > 0)
            _ui.Tip(_fwFaceReset, $"고른 면 → 처음 값({PaintName(FormworkBoard.FirstKindOf(faces[^1].Siblings[0]))}) — 칠한 종류 · 산출 제외 · 판 종류를 이 면만 걷습니다");
    }

    // ── 쉬는 기록 세 갈래 창(계획 §3.3 · 검토 H4) ──

    private Border? _fwWake;
    private TextBlock? _fwWakeText;
    private Action? _fwWakeRetry;
    private string _fwWakeWhat = "";
    private readonly Button?[] _fwWakeButtons = new Button?[3];

    private bool WakeOpen => _fwWake is { Visibility: Visibility.Visible };

    // 쉬는 기록의 수(줄 · 판 · 면 · 자리 · 쪼갬 · 겹침 — 짝 · 칸은 늘 걸려 세지 않음).
    private int SleepingCount()
    {
        var r = _board?.Rules;
        return r == null ? 0 : r.Count + r.DropCount + r.KindCount + r.FaceKindCount + r.PlaceCount + r.SplitCount + r.JointCount;
    }

    private FrameworkElement BuildWakeCard()
    {
        _fwWakeText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new WrapPanel();
        var specs = new[]
        {
            ("다시 쓰고 계속", "BtnPrimary", "btn.fw.wake.keep", "전에 직접 정한 기록을 다시 쓰고(합계가 그 기록대로 바뀝니다) 이 고침을 합니다."),
            ("지우고 계속", "BtnSecondary", "btn.fw.wake.clear", "전에 직접 정한 기록을 지우고(시공 순서표는 남김) 이 고침을 합니다."),
            ("취소", "BtnGhost", "btn.fw.wake.cancel", "아무것도 바꾸지 않습니다(Esc)."),
        };
        for (int k = 0; k < specs.Length; k++)
        {
            var (text, kind, id, tip) = specs[k];
            int choice = k == 0 ? 1 : k == 1 ? 2 : 0;
            var button = _ui.Button(text, kind, tip, id);
            button.Focusable = false;
            button.MinHeight = 28;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Click += (_, _) => AnswerWake(choice);
            _fwWakeButtons[choice] = button;
            buttons.Children.Add(button);
        }
        var stack = new StackPanel();
        stack.Children.Add(_fwWakeText);
        stack.Children.Add(buttons);
        _fwWake = new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, Child = stack, Visibility = Visibility.Collapsed,
        };
        _ui.Mark(_fwWake, "", "fw.wake");
        return _fwWake;
    }

    // 쉬는 기록 때문에 고치지 못했다: 세 갈래 창을 띄우고 정하면 retry(같은 일)를 다시 한다.
    private void AskWake(string what, Action retry)
    {
        if (_fwWake == null || _fwWakeText == null || _board == null) return;
        _fwWakeRetry = retry;
        _fwWakeWhat = what;
        _fwWakeText.Text = $"쓰지 않는 직접 정한 기록 {SleepingCount()}개가 있습니다";
        _ui.Tip(_fwWakeText, $"전에 직접 정한 줄 · 판 · 면 기록이 쉬고 있습니다(규칙대로 셈). 고치려면 먼저 정하세요 — {what}");
        _fwWake.Visibility = Visibility.Visible;
        if (_fwTool == CutTool.Paint) EndPaintDrag();
        Log?.Invoke($"② 쉬는 기록 묻기\t{what} · 기록 {SleepingCount()}");
    }

    // 세 갈래 창의 답: 0 = 취소 · 1 = 다시 쓰고 계속 · 2 = 지우고 계속. 정하면 고침과 함께 되돌리기 한 줄(Wake 전 기록 글로) · 저장은 고침의 한 번.
    private void AnswerWake(int choice)
    {
        var retry = _fwWakeRetry;
        string what = _fwWakeWhat;
        _fwWakeRetry = null;
        if (_fwWake != null) _fwWake.Visibility = Visibility.Collapsed;
        if (_board == null || retry == null || choice == 0)
        {
            Log?.Invoke($"② 쉬는 기록\t취소 · {what}");
            RefreshAll();
            ShowMessage("취소했습니다 — 바뀐 것 없음.");
            return;
        }
        bool keep = choice == 1;
        string wake = keep ? "기록 다시 쓰기" : "기록 지우기";
        string snapshot = _board.Snapshot();
        int undo = _fwUndo.Count;
        _board.Wake(keep);
        Log?.Invoke($"② 쉬는 기록\t{wake} · {what} · 거푸집 {M2(_board.Total)} ㎡");
        retry();
        if (_fwUndo.Count > undo)
        {
            // 고침이 되돌리기를 적었다 — Wake 까지 한 줄로(되돌리면 쉬는 기록 그대로).
            string last = _fwUndo[^1].what;
            _fwUndo.RemoveRange(undo, _fwUndo.Count - undo);
            RecordUndo($"{wake} · {last}", snapshot);
            RefreshAll();
        }
        else
        {
            // 고침 자체는 바뀐 것이 없다(다시 쓴 기록이 이미 그 값) — Wake 만 한 줄 · 저장 한 번.
            RecordUndo(wake, snapshot);
            FinishChange($"② 쉬는 기록\t{wake} · 고침은 바뀐 것 없음", $"{wake} · 거푸집 {M2(_board.Total)} ㎡", wake);
        }
    }

    // 경고 줄의 [다시 쓰기] · [지우기](고침 없이 — 되돌리기 한 줄 · 저장 한 번).
    private bool WakeNow(bool keep)
    {
        if (_board == null || Busy || !_board.Sleeping) return false;
        string snapshot = _board.Snapshot();
        _board.Wake(keep);
        string what = keep ? "기록 다시 쓰기" : "기록 지우기";
        RecordUndo(what, snapshot);
        FinishChange($"② 쉬는 기록\t{what}", $"{what} · 거푸집 {M2(_board.Total)} ㎡", what);
        return true;
    }

    // ── 나무 줄 끝 종류 글 → 종류 다섯 작은 판(계획 §3.4) ──

    private Canvas? _fwKindLayer;
    private Border? _fwKindPanel;
    private LocationTree.Node? _fwKindNode;
    private readonly Dictionary<string, Button> _fwKindPanelButtons = new(StringComparer.Ordinal);

    private bool KindPanelOpen => _fwKindLayer is { Visibility: Visibility.Visible };

    private FrameworkElement BuildTreeKindLayer()
    {
        var column = new StackPanel();
        foreach (string item in FormworkTable.Items)
        {
            string choice = item;
            var (r, g, b) = PaintColor(item);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = Swatch(r, g, b, 5);
            dot.Width = dot.Height = 9;
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = PaintName(item), VerticalAlignment = VerticalAlignment.Center });
            var button = _ui.Button(PaintName(item), "BtnGhost", item == FormworkRules.NotCounted ? "이 면을 산출에서 제외합니다" : $"이 면을 «{item}»으로 합니다", "btn.fw.tree.kind." + item);
            button.Content = content;
            button.Focusable = false;
            button.MinHeight = 26;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(8, 0, 10, 1);
            button.Click += (_, _) => PickPanelKind(choice);
            _fwKindPanelButtons[item] = button;
            column.Children.Add(button);
        }
        _fwKindPanel = new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = column, MinWidth = 110,
        };
        _ui.Mark(_fwKindPanel, "", "fw.tree.kinds");
        _fwKindLayer = new Canvas { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, ClipToBounds = true };
        _fwKindLayer.Children.Add(_fwKindPanel);
        // 작은 판 밖을 누르면 닫는다(누름은 그 줄로 가지 않는다 — 사람이 닫으려 누른 것).
        _fwKindLayer.MouseLeftButtonDown += (_, e) => { if (!_fwKindPanel.IsMouseOver) { CloseKindPanel(); e.Handled = true; } };
        return _fwKindLayer;
    }

    // 그 줄의 종류 글 아래에 작은 판을 띄운다(아래가 모자라면 위로).
    private void OpenKindPanel(LocationTree.Node node, FrameworkElement anchor)
    {
        if (_board == null || Busy || _fwKindLayer == null || _fwKindPanel == null || node.Face == null) return;
        _fwKindNode = node;
        string now = FaceKind(node.Face);
        foreach (var (item, button) in _fwKindPanelButtons) button.Style = _ui.StyleOf(PaintName(item) == now ? "BtnSoft" : "BtnGhost");
        _fwKindLayer.Visibility = Visibility.Visible;
        _fwKindLayer.UpdateLayout();
        var at = anchor.TranslatePoint(new Point(0, anchor.ActualHeight + 2), _fwKindLayer);
        double h = _fwKindPanel.ActualHeight, w = _fwKindPanel.ActualWidth;
        double top = at.Y + h > _fwKindLayer.ActualHeight - 4 ? Math.Max(0, at.Y - anchor.ActualHeight - 4 - h) : at.Y;
        Canvas.SetLeft(_fwKindPanel, Math.Max(0, Math.Min(at.X, _fwKindLayer.ActualWidth - w - 4)));
        Canvas.SetTop(_fwKindPanel, top);
        Log?.Invoke($"② 나무 종류 고르기\t부재 {node.Face.ElementId} · {node.Face.Name} · 현재 {now}");
    }

    private void CloseKindPanel()
    {
        if (_fwKindLayer != null) _fwKindLayer.Visibility = Visibility.Collapsed;
        _fwKindNode = null;
    }

    private void PickPanelKind(string item)
    {
        var node = _fwKindNode;
        CloseKindPanel();
        if (node?.Face is { } face) PaintFaces(new[] { face }, item, "나무");
    }

    // ── 시험에서 쓰는 길 ──

    /// <summary>R73b: 3D 도구 칸의 [칠하기] · 그 아래 종류 줄 · 종류 조각(기록 값 — 산출 제외 = «세지 않음») · 고른 붓 종류 · 마우스 밑 면 · 끌어 지나간 면.</summary>
    public RadioButton? PaintToolButton => _fwToolPaint;
    public FrameworkElement? PaintKinds => _fwPaintKinds;
    public RadioButton? PaintKindButton(string item) => _fwPaintKindButtons.TryGetValue(item, out var b) ? b : null;
    public string PaintKind => _fwPaintKind;
    public BoardFace? PaintSpot => _fwPaintSpot;
    public IReadOnlyList<BoardFace> PaintDragFaces => _fwPaintDrag;

    /// <summary>R73b: 화면의 그 자리에서 붓이 칠할 면(누르기와 같은 찾기 — 없으면 null).</summary>
    public BoardFace? PaintFaceAt(Point at)
    {
        var (index, triangle) = _view3d.HitAt(at);
        return HitFace(index, triangle, at);
    }

    /// <summary>R73b: 판 띠 [↺ 처음 값] · 세 갈래 창(떠 있는가 · 단추 0 취소 · 1 다시 쓰고 계속 · 2 지우고 계속) · 나무 종류 작은 판(떠 있는가 · 그 종류 단추).</summary>
    public Button? FaceResetButton => _fwFaceReset;
    public bool WakeShown => WakeOpen;
    public Button? WakeButton(int choice) => choice is >= 0 and < 3 ? _fwWakeButtons[choice] : null;
    public bool TreeKindPanelShown => KindPanelOpen;
    public Button? TreeKindButton(string item) => _fwKindPanelButtons.TryGetValue(item, out var b) ? b : null;
}
