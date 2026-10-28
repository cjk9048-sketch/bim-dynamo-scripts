using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents; // TextElement
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DH.Takeoff.Core;
using DH.Takeoff.Core.Formwork;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>
/// 「수량산출」 창의 화면(JACK 2026-10-15 «위치·부위 정하기 · 산출식 자동입력 · 산출근거 엑셀은 결국 수량산출 — 한 버튼 · 한 창»).
/// 걸음 둘을 같은 창에서 간다 — 3D 그림은 그 자리 그대로이고(카메라도 그대로), 나머지만 바꿔 놓는다.
///  ① 부위 · 중분류: 3D 에서 부재를 고르고 왼쪽 판에서 넣는다(넣는 즉시 모델의 속성에 적힌다).
///  ② 거푸집면 설정: 대응표(유형 × 면 → 종류)를 고치면 3D 의 면 색과 합계가 바로 바뀐다(PlacePartView.Formwork.cs).
/// ① 의 짜임(JACK 2026-10-13 · 10-15):
///  · 고른 부재 = 3D 그림 바로 위의 띠(몇 개 · 무슨 부재 · 지금 값 · 선택 풀기 · 값 지우기).
///  · 넣기 = 왼쪽 판만: 구조물 종류(대표 구조물) → 부위(그 구조물의 대표 목록 — PartCatalog) → 중분류 카테고리(전의 '위치').
///    중분류는 입력칸 하나: 누르면 이미 만든 이름과 추천 이름이 펼쳐지고, 고르면 바로 들어간다 · 새 이름은 적고 [넣기].
///    [단독객체] = 어느 중분류에도 속하지 않는 부재(전의 '위치 없음'). 색깔 목록은 3D 아래의 '지금 적용 상태'에만 둔다.
///  · 지금 적용 상태 = 3D 그림 바로 아래의 띠: 중분류별 색 + 수 · 부위별 수. 누르면 그 값인 부재를 고르기만 한다.
/// [목록 고치기]: 이름 · 차례 · 지우기(✕). 쓰는 부재가 있는 이름의 ✕ 는 그 줄에서 한 번 묻고, 지우면서 그 부재들의 그 칸도 비운다.
/// '무근' · '중분류 생략'(산출서를 적는 방식)은 [목록 고치기]를 켰을 때만 보인다.
/// 색: 옅은 회색 = 아직 · 중분류 색 = 끝 · 옅은 색 = 덜 끝남(하나만 넣었거나, 창 밖에서 들어온 값이라 확인 필요).
/// 모양은 UiTheme 의 틀을 쓴다. 상태는 전부 Core(PickEditor · FormworkBoard)가 들고 있고, 여기서는 그리기와 전하기만 한다.
/// Revit API 를 쓰지 않는다(tools/uipreview 로 그림을 보고, 진짜 창에 마우스 신호를 넣어 눌러 본다).
/// </summary>
public sealed partial class PlacePartView
{
    private const double SideWidth = 436, PartsMinHeight = 250;   // 부위 칸의 가장 작은 높이(머리 + 목록 대여섯 줄 + [추가] 줄)

    private readonly PickEditor _editor;
    private readonly IReadOnlyList<PickMember> _members;
    private readonly IReadOnlyList<MeshData> _memberMeshes;      // ① 의 겉면(② 에서 돌아올 때 다시 쓴다)
    private readonly UiTheme _ui;
    private readonly Brush _text, _sub, _line, _warn;
    private readonly double _shift;                    // 레벨의 표고(화면에 보이는 값) − 모델 안쪽 높이
    private readonly Model3DView _view3d;
    private readonly TextBlock _remain, _total, _pickedHead, _pickedHint, _hover, _message, _status, _counts, _countNotes, _noPlaceLabel;
    private readonly Border _hoverPill, _toast, _side, _top, _bottom;
    private readonly FrameworkElement _progress, _placeSide, _pickedBand, _stateBand;
    private readonly ColumnDefinition _barDone = new(), _barRest = new();
    private readonly WrapPanel _pickedValues = new(), _statusPlaces = new(), _statusParts = new(), _statusFlags = new();
    private readonly StackPanel _placeRows = new(), _partRows = new();

    // 목록 칸(이름 · 규격 · 타설)의 적다 만 글을 버리는 손잡이 — ✕ · [되돌리기]가 일하기 전에 부른다. 목록을 다시 지을 때 비우고 새 칸이 건다
    // (다른 까닭으로 다시 그려질 때는 전처럼 칸을 떠날 때 적는다 — revit-verify R49 M1).
    private readonly List<Action> _dropPending = new();

    private void DropPending()
    {
        foreach (var drop in _dropPending) drop();
        _dropPending.Clear();
    }
    private readonly ScrollViewer _placeScroll;
    private readonly Button _undo, _confirm, _clear, _sameType, _unpick, _auto, _refill, _putPlace, _noPlace, _next, _back, _finish, _review, _hide, _showAll;
    private readonly TextBox _newPart;
    private readonly ComboBox _place;
    private readonly ToggleButton _editPlaces, _editParts, _onlyRemaining;
    private readonly ComboBox _structure;
    private readonly (Border pill, TextBlock text)[] _steps = new (Border, TextBlock)[4];
    private string _countsText = "", _pickedText = "";
    private (string kind, string name)? _asking;       // ✕ 를 눌러 지울지 묻고 있는 줄(쓰는 부재가 있는 이름)
    private bool _keepAsking, _busy, _syncing, _nextArmed, _keepArmed;
    private FrameworkElement? _partsBlock;             // 왼쪽 판의 부위 칸(중분류를 지울지 묻는 동안 그 높이를 붙잡는다)
    private double _partsHold;                         // 묻는 동안 붙잡은 부위 칸의 높이(0 = 붙잡지 않음)

    public FrameworkElement Root { get; }
    public Brush Background => _ui["Bg"];
    public Model3DView View3D => _view3d;

    /// <summary>지금 걸음: 0 = ① 부위 · 중분류 · 1 = ② 거푸집면 설정.</summary>
    public int Step { get; private set; }

    /// <summary>[닫기]. (R62 — [처음 상태로 되돌리고 닫기]는 없앴다: 창에서 한 일은 누를 때마다 Revit 실행 취소 목록의 한 줄 — JACK 2026-10-26.)</summary>
    public event Action? Close;

    /// <summary>[다음 단계] — 부르는 쪽이 거푸집을 재어 ShowFormwork 로 넘긴다(남은 부재가 있으면 한 번 더 눌러야 온다).</summary>
    public event Action? Next;

    /// <param name="meshes">부재의 겉면 — editor.Members 와 같은 차례.</param>
    /// <param name="notice">창 머리에 알릴 것(읽지 못한 부재 · 저장된 목록을 읽지 못함 등).</param>
    public PlacePartView(PickEditor editor, IReadOnlyList<MeshData> meshes, bool dark, string notice = "")
    {
        _editor = editor;
        _members = editor.Members;
        _memberMeshes = meshes;
        _ui = new UiTheme(dark);
        _text = _ui["Text"];
        _sub = _ui["Sub"];
        _line = _ui["Line"];
        _warn = _ui["WarnFg"];
        _shift = editor.Levels.Count > 0 ? editor.Levels[0].Shown - editor.Levels[0].Z : 0;

        var dock = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        _dock = dock;
        var root = new Border
        {
            Background = _ui["Bg"], Resources = _ui.Resources, Child = dock, UseLayoutRounding = true, SnapsToDevicePixels = true,
            Focusable = true, FocusVisualStyle = null,
        };
        TextElement.SetFontFamily(root, UiTheme.UiFont);
        TextElement.SetFontSize(root, 13);
        TextElement.SetForeground(root, _text);
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Root = root;

        // ── 머리: 제목 · 걸음 …… 남은 부재(진행 — ① 에서만) ──
        _remain = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _total = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _ui["NeutralFg"] };
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 12) };
        _progress = BuildProgress();
        DockPanel.SetDock(_progress, Dock.Right);
        head.Children.Add(_progress);
        var title = new TextBlock { Text = "수량산출", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(BuildSteps());
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        if (notice.Length > 0)
        {
            var box = new Border
            {
                Background = _ui["WarnBg"], CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = string.Join("\n", notice.Split('\n').Select(n => "⚠ " + n)), Foreground = _warn, TextWrapping = TextWrapping.Wrap, LineHeight = 18 },
            };
            DockPanel.SetDock(box, Dock.Top);
            dock.Children.Add(box);
        }

        // ── 아래: 한 줄 알림 · 단추(걸음마다 바뀐다) ──
        //   ① [되돌리기] [닫기] [다음 단계 →]
        //   ② [← 이전 단계] ……………………… [닫기] [완료]
        var foot = new DockPanel { Margin = new Thickness(2, 12, 0, 0), LastChildFill = true };
        Button FootButton(Button button, double right = 8)
        {
            button.MinHeight = 34;
            button.Margin = new Thickness(0, 0, right, 0);
            return button;
        }
        _next = FootButton(_ui.Button("다음 단계  →", "BtnPrimary", "거푸집면 설정으로 넘어갑니다(넣은 값은 이미 모델에 적혀 있습니다).", "btn.nextStep"), 0);
        _finish = FootButton(_ui.Button("완료", "BtnPrimary", "거푸집 객체를 만들고 산출근거 엑셀을 저장합니다.", "btn.finish", UiTheme.IconCheck), 0);
        // ② → ③ 산출서 확인(R59): 산출식 · 부재 이름 · 거푸집 속성을 모델에 적고(Revit 실행 취소 한 줄 «DH 산출서 준비» — R62) 엑셀과 같은 산출근거를 본다.
        _review = FootButton(_ui.Button("다음 단계  →", "BtnPrimary", "산출식 · 부재 이름 · 거푸집 속성을 모델에 적고 산출근거를 봅니다(Revit 실행 취소로 되돌릴 수 있습니다).", "btn.review"), 0);
        foreach (var primary in new[] { _next, _finish, _review })
        {
            primary.MinWidth = 120;
            primary.FontWeight = FontWeights.SemiBold;
        }
        var close = FootButton(_ui.Button("닫기", "BtnSecondary", "넣은 값은 이미 모델에 적혀 있습니다. 창만 닫습니다.", "btn.close"));
        close.MinWidth = 96;
        _undo = FootButton(_ui.Button("되돌리기", "BtnSecondary", "방금 한 일을 되돌립니다(Ctrl+Z).", "btn.undo", UiTheme.IconUndo));
        _back = FootButton(_ui.Button("←  이전 단계", "BtnSecondary", "부위 · 중분류로 돌아갑니다(거푸집에서 고친 것은 그대로 남습니다).", "btn.prevStep"), 14);
        close.Click += (_, _) => Close?.Invoke();
        // 되돌리기는 글자판을 가져가지 않는다 — 가져가면 적다 만 목록 칸이 먼저 적히고, 되돌리기가 그것을 되돌린다(revit-verify R49 M1).
        _undo.Focusable = false;
        _undo.Click += (_, _) =>
        {
            // ② 에서는 ② 에서 한 일(판 지우기 · 옮기기 · 종류 바꾸기 — R55b) · ① 에서는 모델에 넣은 것 · ③ 에서는 없음(보기만 — 계획 검토 H1).
            if (Step == 2) return;
            if (Step == 1) { FormworkUndo(); return; }
            DropPending();
            Do(_editor.Undo);
        };
        _next.Click += (_, _) => PressNext();
        _back.Click += (_, _) =>
        {
            if (Busy) return;
            // ③ → ② (계획 검토 H1) · ② → ①.
            if (Step == 2 && _board != null) { ShowFormwork(_board); ReviewBack?.Invoke(); return; }
            ShowPlaces();
            Back?.Invoke();
        };
        _finish.Click += (_, _) => { if (!Busy) Finish?.Invoke(); };
        _review.Click += (_, _) => { if (!Busy) Review?.Invoke(); };
        foreach (var button in new[] { _next, _finish, _review, close, _undo })
        {
            DockPanel.SetDock(button, Dock.Right);
            foot.Children.Add(button);
        }
        DockPanel.SetDock(_back, Dock.Left);
        foot.Children.Add(_back);
        _status = new TextBlock { Foreground = _sub, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        foot.Children.Add(_status);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);
        _noticeBar = BuildNoticeBar();
        DockPanel.SetDock(_noticeBar, Dock.Top);
        dock.Children.Add(_noticeBar);

        // ── 왼쪽: 구조물 종류 · 부위 · 중분류 카테고리(② 에서는 거푸집 대응표로 바뀐다) ──
        _structure = _ui.Combo("구조물을 고르면 그 구조물의 대표 부위와 중분류 추천이 나옵니다. 부재에 넣은 값은 바뀌지 않습니다.", "combo.structure");
        _place = _ui.ComboEdit("이름을 적거나 목록에서 고르기", "고른 부재의 중분류(예: 수조부 · 배관실). 목록에서 고르면 바로 들어가고, 새 이름은 적은 뒤 [넣기](Enter).", "combo.place");
        _putPlace = _ui.Button("넣기", "BtnSoft", "적은 이름을 고른 부재에 넣습니다(고른 부재가 없으면 목록에만 보탭니다).", "btn.putPlace", UiTheme.IconAdd);
        _noPlaceLabel = new TextBlock { Text = ZonePlan.NoPlaceLabel, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _noPlace = NoPlaceButton();
        _newPart = _ui.Input("새 부위 이름 (목록에 없는 것)", "목록에 없는 부위 이름(예: 도류벽 · 관받침). 부재를 골라 둔 채로 적으면 바로 그 부재에 들어갑니다.", "input.newPart");
        _editPlaces = _ui.Toggle("목록 고치기", "중분류의 이름을 바꾸거나 차례를 옮기거나 지웁니다(✕). 차례가 산출서와 색의 차례입니다.", "toggle.editPlaces", UiTheme.IconEdit);
        _editParts = _ui.Toggle("목록 고치기", "부위의 이름 · 차례를 고치거나 지웁니다(✕). 산출서를 적는 방식('무근' · '중분류 생략')도 여기서 고칩니다.", "toggle.editParts", UiTheme.IconEdit);
        _auto = _ui.Button("자동 채우기", "BtnSoft", "고른 부재 가운데 부위가 빈 것에, 종류에 따른 이름(벽 → 벽체 · 기둥 → 기둥 · 바닥 → 슬래브 …)을 넣습니다.", "btn.auto");
        _refill = _ui.Button("대표 목록 다시 채우기", "BtnGhost", "지운 대표 부위를 다시 불러옵니다(지금 구조물의 대표 목록에서 빠진 이름만 더합니다).", "btn.refill");
        _placeScroll = new ScrollViewer { Content = _placeRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 190 };
        _placeSide = BuildSide();
        _side = new Border
        {
            Width = SideWidth, Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(16, 12, 6, 12), Child = _placeSide,
        };
        DockPanel.SetDock(_side, Dock.Left);
        dock.Children.Add(_side);

        // ── 오른쪽: 고른 부재(위) · 도구 줄 · 3D 그림 · 지금 적용 상태(아래) ──
        // 층을 나눠 그린다(R48): ① 은 모두 Solid 라 전과 같은 그림이고, ② 는 줄을 고르면 그 줄의 판만 맨 위에 진하게 · 나머지는 옅게.
        _view3d = new Model3DView(meshes, dark, layered: true)
        {
            Shown = i => _editor.IsShown(_members[i]),
            ColorOf = i => ColorOf(_members[i]),
            Picked = i => _editor.Selection.Contains(_members[i].Id),
        };
        _pickedHead = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _pickedHint = new TextBlock { Foreground = _sub, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _unpick = Small("선택 풀기", "고른 것을 모두 풉니다(Esc).", "btn.unpick");
        _sameType = Small("같은 유형 모두", "고른 부재와 종류·유형이 같은 부재를 모두 고릅니다(보이는 것 가운데).", "btn.sameType");
        _clear = Small("값 지우기", "고른 부재의 부위와 중분류를 지워 '아직'으로 되돌립니다.", "btn.clear");
        _confirm = Small("이 값 그대로 확인", "고른 부재에 들어 있는 값(창 밖에서 들어온 값 — 복사한 부재 · 속성 창에서 넣은 값)을 그대로 맞다고 확인합니다. 값을 새로 넣은 칸은 따로 확인하지 않아도 됩니다.", "btn.confirm", "BtnPrimary");
        _onlyRemaining = _ui.Toggle("남은 것만 보기", "끝난 부재를 숨기고 남은 부재만 봅니다 — 바깥부터 정해 가면 안쪽이 드러납니다.", "toggle.onlyRemaining");
        _counts = new TextBlock { Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _countNotes = new TextBlock { Foreground = _sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 17, Visibility = Visibility.Collapsed };

        _hide = Small("숨기기", "고른 부재를 잠깐 숨깁니다 — 지붕이나 외벽을 치우고 안쪽을 볼 때.", "btn.hide");
        _showAll = Small("모두 보이기", "숨긴 부재를 다시 보이고 '남은 것만 보기'도 끕니다.", "btn.showAll");

        // 그림 위 · 아래의 띠는 걸음마다 바꿔 끼운다(① 고른 부재 · 지금 적용 상태 ↔ ② 거푸집 합계 · 색 풀이). 그림과 도구 줄은 그대로다.
        var right = new DockPanel();
        _pickedBand = BuildPicked();
        _top = new Border { Child = _pickedBand };
        DockPanel.SetDock(_top, Dock.Top);
        right.Children.Add(_top);
        var tools = BuildTools();
        DockPanel.SetDock(tools, Dock.Top);
        right.Children.Add(tools);
        _stateBand = BuildState();
        _bottom = new Border { Child = _stateBand };
        DockPanel.SetDock(_bottom, Dock.Bottom);
        right.Children.Add(_bottom);

        // 마우스 밑의 부재: 그림의 왼쪽 아래에 뜨는 딱지(그림을 가리지 않게 마우스는 받지 않는다).
        _hover = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Text = " " };
        _hoverPill = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = _hover,
        };
        // 알림(방금 한 일 · 안 된 까닭): 그림의 왼쪽 위 — 고른 부재 띠 바로 아래라 눈이 가는 자리다(전에는 창의 맨 아래 줄이라 못 봤다).
        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18, FontSize = 12.5 };
        _toast = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10, 10, 330, 10), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = _message,
        };
        // 그림을 둥근 테두리 안에 담는다: 모서리 밖으로 그림이 삐져나오지 않게 둥글게 자른다.
        var stage = new Grid();
        stage.Children.Add(_view3d);
        stage.Children.Add(BuildLook());
        stage.Children.Add(_hoverPill);
        stage.Children.Add(_toast);
        stage.SizeChanged += (_, _) => stage.Clip = new RectangleGeometry(new Rect(0, 0, stage.ActualWidth, stage.ActualHeight), 9, 9);
        right.Children.Add(new Border { BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = stage });
        dock.Children.Add(right);

        _view3d.Clicked += (index, ctrl) =>
        {
            if (Step == 2) { ClickReview(index); return; }
            if (Step == 1) { ClickFormwork(index, ctrl); return; }
            if (index < 0) return;                       // 빈 곳을 찍으면 그대로 둔다(Esc · [선택 풀기]로 푼다)
            _editor.Click(_members[index].Id, ctrl);
            RefreshAll();
        };
        _view3d.Banded += (indices, ctrl) =>
        {
            if (Step == 1) { BandFormwork(indices, ctrl); return; }   // ② 판 여러 장(R66)
            if (Step != 0) return;                       // ③ 에서는 끌어 고르지 않는다(BandSelect 를 끈다 — 여기는 혹시 모를 때)
            _editor.Band(indices.Select(i => _members[i].Id), ctrl);
            RefreshAll();
        };
        _view3d.Hovered += index =>
        {
            _hover.Text = index < 0 ? " " : Step == 2 ? DescribeReview(index) : Step == 1 ? DescribeFormwork(index) : Describe(_members[index]);
            _hoverPill.Visibility = index < 0 || _hover.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        _view3d.Failed += ex => ShowMessage("3D 그림에서 오류가 났습니다(끌던 것은 그만두었습니다): " + ex.Message, true);

        root.PreviewKeyDown += OnKey;
        ShowStep();
        RefreshAll();
    }

    // Esc = 선택 풀기 · Ctrl+Z = 되돌리기. 창에도 건다(BuildWindow) — 글자판이 창 자체에 있을 때도 듣게.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Busy) { e.Handled = true; return; }     // 모델에 적는 중에는 글자판도 받지 않는다
        if (e.OriginalSource is TextBox) return;     // 이름을 적는 중에는 글자판을 가로채지 않는다
        if (e.Key == Key.Escape && InOpenList(e.OriginalSource)) return;     // 펼친 목록(층 · 구조물)에서 Esc 는 그 목록을 닫는다 — 고른 부재는 그대로
        if (Step == 2)
        {
            // ③ : 보기만 — Esc = 고른 줄 풀기 · Del · Ctrl+Z 는 아무것도 하지 않는다(① 의 모델 되돌리기로 흘러가지 않게 — 계획 검토 H1).
            if (e.Key == Key.Escape) { _view3d.CancelDrag(); PickReview(-1); e.Handled = true; }
            else if (e.Key == Key.Delete || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) e.Handled = true;
            return;
        }
        if (Step == 1)
        {
            // ② : Esc = 옮길 줄 고르기 취소 → 고른 줄 풀기 · Del = 찍은 판 지우기 · Ctrl+Z = ② 에서 한 일 되돌리기(R55b — JACK 2026-10-21 «지우기(Del 키도) · 되돌리기»).
            // ① 의 모델 되돌리기로 흘러가지 않게 이 갈래에서 끝낸다(계획 검토 L4).
            if (e.Key == Key.Escape) { _view3d.CancelDrag(); if (!ClosePlaceBar()) PickRow(null); e.Handled = true; }
            else if (e.Key == Key.Delete) { DropSelected(); e.Handled = true; }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { FormworkUndo(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape) { _view3d.CancelDrag(); _editor.ClearSelection(); RefreshAll(); e.Handled = true; }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Do(_editor.Undo); e.Handled = true; }
    }

    // ── 걸음 ─────────────────────────────────────────────────────────────────────

    // 걸음 표시: ① 부위 · 중분류 › ② 거푸집면 설정 › ③ 산출서 확인 › 완료 — 지금 걸음만 강조색 딱지.
    private FrameworkElement BuildSteps()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string[] names = { "① 부위 · 중분류", "② 거푸집면 설정", "③ 산출서 확인", "완료" };
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0) line.Children.Add(new TextBlock { Text = "›", Foreground = _sub, FontSize = 14, Margin = new Thickness(7, 0, 7, 2), VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = names[i], FontSize = 12.5 };
            var pill = _ui.Pill(text, "None");
            _steps[i] = (pill, text);
            line.Children.Add(pill);
        }
        return line;
    }

    // 걸음에 맞게 머리 · 아래 단추 · 도구 줄을 맞춘다(왼쪽 판과 그림 위아래 띠는 ShowFormwork · ShowPlaces 가 바꿔 끼운다).
    private void ShowStep()
    {
        for (int i = 0; i < _steps.Length; i++)
        {
            bool on = i == Step;
            _steps[i].pill.Background = _ui[on ? "AccentSoft" : "None"];
            _steps[i].text.Foreground = on ? _ui["AccentText"] : _sub;
            _steps[i].text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        bool places = Step == 0;
        Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = Show(places);
        _next.Visibility = Show(places);
        _review.Visibility = Show(Step == 1);              // ② → ③ (R59)
        _finish.Visibility = Show(Step == 2);              // [완료]는 ③ 에만
        _back.Visibility = Show(!places);
        _undo.Visibility = Show(Step != 2);                // [되돌리기]는 ① · ② (② 는 ② 에서 한 일 — R55b) · ③ 은 보기만
        // ② 에는 고른 부재가 없다: 숨기기 · 남은 것만 보기는 ① 에서만(① 에서 숨긴 부재는 ② 에서도 숨긴 채다). 종류 · 층은 둘 다.
        _hide.Visibility = _showAll.Visibility = _onlyRemaining.Visibility = Show(places);
        _view3d.BandSelect = places || Step == 1;   // ② 도 끌어 판 여러 장(R66)
        _nextArmed = false;
    }

    // [다음 단계]: 남은 부재가 있으면 한 번은 알리고 멈춘다(그래도 넘어가려면 한 번 더) — 거푸집은 부위와 상관없이 셀 수 있지만,
    // 산출서의 제목이 '구역 미지정' · 자동 이름으로 나가므로 모르고 넘어가지 않게.
    private void PressNext()
    {
        if (Busy) return;
        int remaining = _editor.Progress().Remaining;
        if (remaining > 0 && !_nextArmed)
        {
            _nextArmed = _keepArmed = true;
            RefreshAll();
            ShowMessage($"남은 부재가 {remaining}개 있습니다 — 그래도 넘어가려면 [다음 단계]를 한 번 더 누르세요.", true);
            return;
        }
        _nextArmed = false;
        Next?.Invoke();
    }

    /// <summary>
    /// 모델을 읽는 긴 일(거푸집을 재는 것 등) 하나: 창을 잠그고 기다림 커서를 띄운다. 오류는 받아 그 글을 돌려준다("" = 됨).
    /// 화면은 다시 맞추지 않는다(부르는 쪽이 이어서 걸음을 바꾼다).
    /// </summary>
    public string RunLocked(Action work)
    {
        if (_busy) return "다른 일을 하는 중입니다.";
        _busy = true;
        var before = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        Root.IsHitTestVisible = false;
        try { work(); return ""; }
        catch (Exception ex) { return ex.Message; }
        finally
        {
            Root.IsHitTestVisible = true;
            Mouse.OverrideCursor = before;
            _busy = false;
        }
    }

    // 글쇠가 펼쳐져 있는 '고르는 칸'(이나 그 안의 줄)에서 왔는가.
    private static bool InOpenList(object source)
    {
        var combo = source as ComboBox ?? (source is ComboBoxItem item ? ItemsControl.ItemsControlFromItemContainer(item) as ComboBox : null);
        return combo is { IsDropDownOpen: true };
    }

    // ── 창 ───────────────────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    /// <summary>
    /// 이 화면을 담은 창. Revit 쪽은 주인 창과 닫기 처리만 더한다 — 시험(tools/uipreview)도 같은 창에 마우스 신호를 넣는다.
    /// 크기는 화면에 맞춘다(목록이 길어 높이가 넉넉할수록 좋다).
    /// </summary>
    public Window BuildWindow()
    {
        var area = SystemParameters.WorkArea;
        // 최소 크기는 줄여 쓸 수 있게(R62 — 창을 띄운 채 Revit 도 쓰므로 · 처음 크기는 그대로 크게 — JACK 2026-10-26 «지금처럼 크게 + 기억»).
        double minWidth = Math.Min(900, area.Width), minHeight = Math.Min(620, area.Height);
        var window = new Window
        {
            Title = "DH 수량산출",
            Content = Root,
            Background = Background,
            Width = Math.Clamp(area.Width - 60, minWidth, 1500), Height = Math.Clamp(area.Height - 40, minHeight, 960),
            MinWidth = minWidth, MinHeight = minHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        window.PreviewKeyDown += OnKey;
        // 글자판을 그림에 준다: 창을 열자마자 Esc · Ctrl+Z 가 듣는다.
        window.ContentRendered += (_, _) => { if (!_view3d.IsKeyboardFocusWithin) _view3d.Focus(); };
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
        return window;
    }

    // ── 머리의 진행 ──────────────────────────────────────────────────────────────

    // 남은 부재 n개 [전체 n] ▓▓░░ [다음 남은 부재][남은 부재 모두 고르기]
    private FrameworkElement BuildProgress()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_remain);
        var total = _ui.Pill(_total);
        total.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(total);
        var bar = new Grid { Height = 6, Width = 140, Margin = new Thickness(12, 1, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(_barDone);
        bar.ColumnDefinitions.Add(_barRest);
        var track = new Border { Background = _ui["InputLine"], CornerRadius = new CornerRadius(3) };     // 바탕(창의 바탕색) 위라 조금 짙게
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = _ui["Good"], CornerRadius = new CornerRadius(3) });
        line.Children.Add(bar);
        var next = Small("다음 남은 부재", "남은 부재를 위에 있는 것부터 하나씩 골라 보여 줍니다.", "btn.next");
        var all = Small("남은 부재 모두 고르기", "보이는 것 가운데 남은 부재를 모두 고릅니다.", "btn.allRemaining");
        all.Margin = new Thickness(0);
        next.Click += (_, _) =>
        {
            long? id = _editor.NextRemaining();
            if (id is long found)
            {
                int index = IndexOf(found);
                if (index >= 0) _view3d.ZoomTo(index);
            }
            RefreshAll();
        };
        all.Click += (_, _) => { _editor.SelectWhere(m => m.Remaining, "남은 부재"); RefreshAll(); };
        line.Children.Add(next);
        line.Children.Add(all);
        return line;
    }

    // ── 고른 부재(그림 바로 위) ───────────────────────────────────────────────────

    private FrameworkElement BuildPicked()
    {
        var line = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _unpick.Margin = new Thickness(0);
        _unpick.Click += (_, _) => { _editor.ClearSelection(); RefreshAll(); };
        _sameType.Click += (_, _) => { _editor.SelectSameType(); RefreshAll(); };
        _clear.Click += (_, _) => Do(_editor.ClearValues);
        _confirm.Click += (_, _) => Do(_editor.Confirm);
        foreach (var button in new[] { _confirm, _sameType, _clear, _unpick }) buttons.Children.Add(button);
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(buttons);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_pickedHead);
        var values = new Grid { Margin = new Thickness(0, 5, 0, 0), MinHeight = 22 };
        values.Children.Add(_pickedHint);
        values.Children.Add(_pickedValues);
        texts.Children.Add(values);
        line.Children.Add(texts);
        return new Border
        {
            Background = _ui["Card"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 9, 12, 10), MinHeight = 66, Child = line,
        };
    }

    // ── 왼쪽 판 ──────────────────────────────────────────────────────────────────

    // 왼쪽 판(① 걸음): 구조물 종류 → 부위(남는 자리 전부) → 중분류 카테고리(입력칸 · [넣기] · [단독객체]).
    private FrameworkElement BuildSide()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 구조물 종류
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = PartsMinHeight });   // 부위(남는 자리 전부)
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 중분류 카테고리(고치는 중이면 줄 수만큼 — 많으면 그 안에서 굴린다)

        // 구조물 종류
        var top = new StackPanel();
        var caption = new TextBlock { Text = "구조물 종류", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 7) };
        top.Children.Add(caption);
        var structure = new DockPanel();
        foreach (var s in PartCatalog.Structures) _structure.Items.Add(s.Name);
        _structure.SelectedItem = _editor.Structure;
        _structure.SelectionChanged += (_, _) =>
        {
            if (_syncing || _structure.SelectedItem is not string to || to == _editor.Structure) return;
            Do(() => _editor.SetStructure(to));
            _view3d.Focus();       // 글자판을 그림으로 돌린다: 고르는 칸에 남아 있으면 화살표 · 굴림에 구조물이 또 바뀐다
        };
        // 닫혀 있는 고르는 칸 위에서 굴려도 구조물이 바뀌지 않게(바뀔 때마다 모델에 목록을 적는다).
        _structure.PreviewMouseWheel += (_, e) => { if (!_structure.IsDropDownOpen) e.Handled = true; };
        // 펼쳤다가 고르지 않고 닫았을 때(Esc · 바깥 누르기 · 같은 줄)도 글자판을 그림으로 돌린다 — 칸에 남아 있으면 그 뒤의 ↑↓ 한 번이
        // 뜻하지 않게 구조물을 바꾼다(검토 R26).
        _structure.DropDownClosed += (_, _) => _view3d.Focus();
        structure.Children.Add(_structure);
        top.Children.Add(structure);
        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        // 부위
        var parts = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var rule2 = Rule();
        DockPanel.SetDock(rule2, Dock.Top);
        parts.Children.Add(rule2);
        var partHead = HeadLine("부위", "", _editParts, _auto);
        DockPanel.SetDock(partHead, Dock.Top);
        parts.Children.Add(partHead);
        var addPart = AddLine(_newPart, "btn.addPart", () =>
        {
            string text = _newPart.Text;
            Do(() => _editor.Selection.Count > 0 ? _editor.SetPart(text) : _editor.AddPart(text), ok => { if (ok) _newPart.Text = ""; });
        });
        DockPanel.SetDock(addPart, Dock.Bottom);
        parts.Children.Add(addPart);
        _refill.MinHeight = 24;
        _refill.FontSize = 12;
        _refill.Padding = new Thickness(8, 0, 8, 1);
        _refill.HorizontalAlignment = HorizontalAlignment.Left;
        _refill.Margin = new Thickness(0, 4, 0, 0);
        _refill.Click += (_, _) => Do(() => _editor.SetStructure(_editor.Structure));
        DockPanel.SetDock(_refill, Dock.Bottom);
        parts.Children.Add(_refill);
        _auto.Click += (_, _) => Do(_editor.FillAutoPart);
        parts.Children.Add(new ScrollViewer { Content = _partRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _editParts.Click += (_, _) => RefreshAll();
        Grid.SetRow(parts, 1);
        grid.Children.Add(parts);

        // 중분류 카테고리: [이름 칸 ▾][넣기] · [단독객체]. 고치는 중이면 그 위에 이름 · 차례 · 지우기 줄(색 = 3D 의 색).
        var places = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        places.Children.Add(Rule());
        places.Children.Add(HeadLine("중분류 카테고리", "", _editPlaces));
        places.Children.Add(_placeScroll);
        var put = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        _putPlace.Margin = new Thickness(6, 0, 0, 0);
        _putPlace.Click += (_, _) => PutPlace(_place.Text);
        DockPanel.SetDock(_putPlace, Dock.Right);
        put.Children.Add(_putPlace);
        put.Children.Add(_place);
        places.Children.Add(put);
        places.Children.Add(_noPlace);
        WirePlaceBox(put);
        _editPlaces.Click += (_, _) => RefreshAll();
        Grid.SetRow(places, 2);
        grid.Children.Add(places);

        // 창이 낮아 판에 다 들어가지 않으면(작은 화면 · 큰 배율) 판 전체를 굴린다 — 아래쪽 줄이 잘려 안 보이는 일이 없게.
        // 판의 높이 = 보이는 높이와 «구조물 종류 + 중분류 칸 + 부위 칸의 가장 작은 높이» 가운데 큰 쪽. 중분류 칸은 고치는 중에 따라
        // 키가 달라지므로 그때마다 다시 잰다(정해 둔 한 값으로는 고치는 중에 부위 칸의 아래가 잘릴 수 있었다 — 검토 R26).
        // 중분류를 지울지 묻는 동안에는 부위 칸의 높이를 붙잡는다: 중분류 칸은 판의 맨 아래라, 묻는 줄이 커지는 만큼 부위 칸이 줄면
        // 묻는 줄이 ✕ 를 누른 자리에서 위로 밀려 뜬다(2026-10-15 시험이 60px 로 잼) — 붙잡으면 판이 아래로 늘어나 그 자리에 뜬다.
        _partsBlock = parts;
        var scroller = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        void Fit() => grid.Height = Math.Max(scroller.ActualHeight, top.ActualHeight + parts.Margin.Top + places.ActualHeight + places.Margin.Top + Math.Max(PartsMinHeight, _partsHold));
        scroller.SizeChanged += (_, _) => Fit();
        top.SizeChanged += (_, _) => Fit();
        places.SizeChanged += (_, _) => Fit();
        return scroller;
    }

    // [단독객체]: 어느 중분류에도 속하지 않는 것으로 정하는 단추(아직 안 정한 것과 다르다) — 고른 부재가 모두 그렇게 정해져 있으면 켜져 보인다.
    private Button NoPlaceButton()
    {
        var content = new DockPanel { LastChildFill = true };
        var chip = Swatch(ZonePalette.NoPlace.r, ZonePalette.NoPlace.g, ZonePalette.NoPlace.b, 7);
        DockPanel.SetDock(chip, Dock.Left);
        content.Children.Add(chip);
        content.Children.Add(_noPlaceLabel);
        var button = new Button { Style = _ui.StyleOf("BtnPick"), Content = content, Tag = "", Margin = new Thickness(0, 6, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 132 };
        _ui.Mark(button, "", "btn.noPlace");
        button.Click += (_, _) => Do(_editor.SetNoPlace);
        return button;
    }

    // 중분류 입력칸: 누르면 목록이 펼쳐진다(이미 만든 이름 · 추천 이름). 목록에서 고르면 바로 넣고, 적은 이름은 Enter · [넣기].
    // holder = 칸을 담은 판(칸의 바깥 — Enter 를 칸보다 먼저 받는 자리).
    private void WirePlaceBox(UIElement holder)
    {
        _place.MaxDropDownHeight = 300;
        // Enter = 칸에 보이는 이름을 넣는다(친 이름 · ↓ 로 짚어 칸에 들어온 이름). 칸을 담은 판에서 미리 받는다:
        // 목록이 펼쳐진 채 마우스가 어느 줄 위에 있으면, 글을 치는 사이 WPF 가 그 줄을 짚는다(잰 값: 펼친 뒤 · 마우스를 옮긴 뒤에는 짚힌 줄 없음 →
        // 친 뒤 «고른 줄 −1 · 짚힌 줄 있음»). 적는 칸이 있는 고르는 칸(ComboBox)은 Enter 를 미리보기 단계에서 스스로 먼저 받아 짚힌 줄을 굳히고
        // (KeyDownHandler → CommitSelection) 칸의 글을 그 줄의 이름으로 바꾼다 — 그 뒤에 칸을 읽던 전의 처리기는 친 이름 대신 그 줄을 넣었다
        // (JACK 2026-10-16 15시 «쳐 넣어도 Enter 치면 목록에 선택된 애로 들어가져» — 진짜 마우스 시험으로 재현: 칸에 보인 글 «마우스 아래 새 이름» → 들어간 것 «수조부»).
        // 미리보기 신호는 바깥에서 안으로 내려오므로 판이 칸보다 먼저 받는다 — 여기서 처리함으로 막으면 칸의 굳히기는 돌지 않는다. 목록은 굳히지 않고 닫는다.
        // 한글 입력기가 글자를 짜는 중의 Enter 는 Key.ImeProcessed 로 온다(revit-verify R48 계획 L2) — 그 Enter 로 짜던 글자가 칸에 들어가므로
        // 다 들어간 뒤(BeginInvoke)에 칸을 읽는다. 빈 칸의 Enter(넣고 비운 뒤 한 번 더 오는 Enter 등)는 아무 일도 하지 않고 지나간다.
        holder.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            bool composing = e.Key == Key.ImeProcessed && e.ImeProcessedKey == Key.Enter;
            if ((e.Key != Key.Enter && !composing) || !Within(e.OriginalSource as DependencyObject, _place)) return;
            if (_place.IsDropDownOpen) _place.IsDropDownOpen = false;
            e.Handled = true;
            if (composing) _place.Dispatcher.BeginInvoke(() => PutShownPlace(), System.Windows.Threading.DispatcherPriority.Input);
            else PutShownPlace();
        }));
        // 글 칸을 누르면 펼친다(꺾쇠를 누를 때는 그 단추가 스스로 펼치고 닫는다 — 여기서 또 펼치면 바로 닫힌다).
        // WPF 의 고르는 칸은 글자판이 없을 때 글 칸을 누르면 그 누름을 '처리함'으로 막고 글자판만 가져간다 — 처리된 누름도 듣게 건다
        // (보통으로 걸면 처리기가 아예 불리지 않는다: 2026-10-15 계측 — 창에는 누름이 왔는데 칸의 처리기는 0번).
        // 펼친 채 글 칸을 다시 눌러도 닫지 않는다(StaysOpenOnEdit) — 적던 이름을 고치려고 누르는 것이다.
        _place.StaysOpenOnEdit = true;
        _place.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || Busy || _place.IsDropDownOpen || _place.Items.Count == 0) return;
            if (_place.Template?.FindName("PART_EditableTextBox", _place) is not TextBox box || !Within(e.OriginalSource as DependencyObject, box)) return;
            _place.IsDropDownOpen = true;
        }), handledEventsToo: true);
        // 닫혀 있는 칸 위에서 굴려도 고른 줄이 바뀌지 않고 판이 굴러간다(판을 굴리던 손이 칸 위를 지날 때).
        _place.PreviewMouseWheel += (_, e) => PassWheel(_place, e);
    }

    // 중분류 칸에 지금 보이는 글(친 이름 · ↓ 로 짚어 칸에 들어온 이름)을 넣는다 — 빈 칸이면 아무 일도 하지 않는다.
    private void PutShownPlace()
    {
        string shown = _place.Template?.FindName("PART_EditableTextBox", _place) is TextBox box ? box.Text : _place.Text;
        if (string.IsNullOrWhiteSpace(shown)) return;
        PutPlace(shown);
    }

    private static bool Within(DependencyObject? node, DependencyObject ancestor)
    {
        for (var at = node; at != null; at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    // 중분류 넣기: 부재를 골라 두었거나 목록에 있는 이름이면 고른 부재에 넣는다(고른 것이 없으면 '먼저 고르세요'를 알린다).
    // 고른 것이 없고 새 이름이면 목록에만 보탠다. 들어가면 칸을 비운다(안 되면 적은 이름을 그대로 둔다).
    private void PutPlace(string text)
    {
        string name = ZonePlan.CleanName(text);
        bool known = _editor.Places.Any(p => p.Name == name);
        Do(() => _editor.Selection.Count > 0 || known ? _editor.SetPlace(name) : _editor.AddPlace(name), ok => { if (ok) SetPlaceText(""); });
    }

    private void SetPlaceText(string text)
    {
        _syncing = true;
        _place.SelectedIndex = -1;
        _place.Text = text;
        _syncing = false;
    }

    // 그림 위의 도구 줄: 볼 종류 · 볼 층 · 숨기기 · 남은 것만.
    private FrameworkElement BuildTools()
    {
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };

        string[] kindNames = { "전체", "바닥·기초", "벽", "기둥·보", "그 밖" };
        var kinds = new UIElement[kindNames.Length];
        for (int i = 0; i < kindNames.Length; i++)
        {
            var kind = (KindFilter)i;
            var segment = _ui.Segment(kindNames[i], "그림에서 볼 부재의 종류 — 보이는 부재만 찍을 수 있습니다.", "kind." + i);
            segment.IsChecked = _editor.Kind == kind;
            segment.Checked += (_, _) =>
            {
                if (_editor.Kind == kind) return;
                _editor.SetKind(kind);
                RefreshAll();
            };
            kinds[i] = segment;
        }
        tools.Children.Add(Caption("종류"));
        tools.Children.Add(_ui.Segments(kinds));

        var floors = _ui.Combo("그림에서 볼 층(레벨 사이).", "combo.floor");
        floors.Width = 210;
        floors.Margin = new Thickness(0, 0, 16, 0);
        floors.Items.Add("전체");
        foreach (var band in _editor.Floors) floors.Items.Add(band.Name);
        floors.SelectedIndex = _editor.Floor + 1;
        floors.SelectionChanged += (_, _) => { _editor.SetFloor(floors.SelectedIndex - 1); RefreshAll(); };
        floors.IsEnabled = _editor.Floors.Count > 0;
        tools.Children.Add(Caption("층", 16));
        tools.Children.Add(floors);

        _hide.Click += (_, _) => { _editor.HideSelection(); RefreshAll(); };
        _showAll.Click += (_, _) => { _editor.ShowAll(); RefreshAll(); };
        tools.Children.Add(_hide);
        tools.Children.Add(_showAll);
        _onlyRemaining.IsChecked = _editor.OnlyRemaining;
        _onlyRemaining.Click += (_, _) => { _editor.SetOnlyRemaining(_onlyRemaining.IsChecked == true); RefreshAll(); };
        tools.Children.Add(_onlyRemaining);
        return tools;
    }

    // 그림의 오른쪽 위에 떠 있는 줄: 보는 방향 · 화면 맞춤.
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
            button.Click += (_, _) => _view3d.Look(captured);
            line.Children.Add(button);
        }
        line.Children.Add(new Border { Width = 1, Background = _line, Margin = new Thickness(4, 5, 4, 5) });
        var fit = _ui.Button("화면 맞춤", "BtnSeg", "모든 부재가 다 보이게 맞춥니다(가운데 단추를 두 번 눌러도 됩니다).", "btn.fit");
        fit.MinHeight = 26;
        fit.Padding = new Thickness(9, 0, 9, 1);
        fit.Focusable = false;
        fit.Click += (_, _) => _view3d.Fit();
        line.Children.Add(fit);
        return new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(3),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = line,
        };
    }

    // 그림 바로 아래: 지금 적용 상태(위치별 색 + 수 · 부위별 수 — 누르면 그 부재를 고른다) · 색 풀이 · 마우스 쓰는 법.
    private FrameworkElement BuildState()
    {
        var box = new StackPanel { Margin = new Thickness(2, 10, 2, 0) };

        // 좁으면 마우스 쓰는 법(오른쪽 글)이 먼저 줄어든다 — 센 수는 끝까지 보인다.
        var head = new DockPanel();
        var title = new TextBlock { Text = "지금 적용 상태", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        DockPanel.SetDock(_counts, Dock.Left);
        head.Children.Add(_counts);
        head.Children.Add(new TextBlock
        {
            Text = "굴림 = 확대 · 가운데 단추 끌기 = 옮기기 · 오른쪽 단추 끌기 = 돌리기",
            Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0), Opacity = 0.9, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        box.Children.Add(head);

        FrameworkElement Line(string name, WrapPanel chips, string tip)
        {
            var line = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var label = new TextBlock { Text = name, Foreground = _sub, FontSize = 12, Width = 46, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            _ui.Tip(label, tip);
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);
            line.Children.Add(chips);
            return line;
        }
        var lines = new StackPanel();
        lines.Children.Add(Line("중분류", _statusPlaces, "중분류마다 그림의 색과 부재 수 — 딱지를 누르면 그 중분류인 부재를 고릅니다."));
        lines.Children.Add(Line("부위", _statusParts, "부위마다 부재 수 — 딱지를 누르면 그 부위인 부재를 고릅니다."));
        // 중분류 · 부위가 많으면 띠가 그림을 밀어내지 않게 세 줄쯤에서 굴린다.
        box.Children.Add(new ScrollViewer { Content = lines, MaxHeight = 88, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });

        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(_statusFlags, Dock.Right);
        foot.Children.Add(_statusFlags);
        var legend = new WrapPanel();
        void Item(FrameworkElement swatch, string text)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = text, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(item);
        }
        var first = ZonePalette.Of(0);
        var pale = ZonePalette.Pale(first);
        Item(Swatch(first.r, first.g, first.b), "진한 색 = 부위·중분류 다 넣음");
        Item(Swatch(pale.r, pale.g, pale.b), "옅은 색 = 하나만 넣었거나 확인 필요");
        Item(Swatch(ZonePalette.PartOnly.r, ZonePalette.PartOnly.g, ZonePalette.PartOnly.b), "부위만");
        Item(new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Brushes.White, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(ZonePalette.Picked.r, ZonePalette.Picked.g, ZonePalette.Picked.b)),
        }, "고른 부재");
        foot.Children.Add(legend);
        box.Children.Add(foot);
        box.Children.Add(_countNotes);
        return box;
    }

    // ── 왼쪽 목록 ────────────────────────────────────────────────────────────────

    private void RefreshLists()
    {
        var picked = _editor.Selected.ToList();
        bool any = picked.Count > 0;
        string StateOf(Func<PickMember, bool> has) => !any ? "" : picked.All(has) ? "on" : picked.Any(has) ? "some" : "";

        _syncing = true;
        _structure.SelectedItem = _editor.Structure;
        _syncing = false;

        // 중분류 카테고리: 고치는 중이면 이름 · 차례 · 지우기 줄(색 = 3D 의 색). 입력칸의 목록 · [단독객체]는 늘 있다.
        _dropPending.Clear();   // 다시 짓는 칸들(중분류 · 부위)이 새로 건다
        _placeRows.Children.Clear();
        bool editPlaces = _editPlaces.IsChecked == true;
        _placeScroll.Visibility = editPlaces ? Visibility.Visible : Visibility.Collapsed;
        if (editPlaces)
        {
            if (_editor.Places.Count == 0) _placeRows.Children.Add(Note("아직 중분류가 없습니다. 아래 칸에 이름을 적어 넣으세요."));
            for (int i = 0; i < _editor.Places.Count; i++)
            {
                string name = _editor.Places[i].Name;
                var c = ZonePalette.Of(i);
                if (_asking == ("place", name)) { _placeRows.Children.Add(AskRow("place", name)); continue; }
                _placeRows.Children.Add(EditRow("place." + name, name, Swatch(c.r, c.g, c.b, 8), null,
                    i > 0, i + 1 < _editor.Places.Count, step => Do(() => _editor.MovePlace(name, step)), () => AskDelete("place", name),
                    text => Do(() => _editor.RenamePlace(name, text))));
            }
        }
        RefreshPlaceChoices();
        _noPlace.Tag = StateOf(m => m.Value.Free);
        _noPlace.Opacity = any ? 1 : 0.6;
        _noPlaceLabel.Foreground = (string)_noPlace.Tag == "on" ? _ui["AccentText"] : _sub;
        _ui.Tip(_noPlace, (any ? $"고른 부재 {picked.Count}개를 " : "고른 부재를 ") +
                          "어느 중분류에도 속하지 않는 부재(구조물 전체에 걸친 버림 콘크리트 등)로 정합니다 — 아직 안 정한 것과 다르고, 산출서 제목에 중분류를 붙이지 않습니다.");

        // 부위: 묶음마다 [묶음 이름 | 단추 두 줄]. 고치는 중이면 한 줄에 하나씩(이름 칸 · 무근 · 중분류 생략 · 차례 · 지우기).
        _partRows.Children.Clear();
        bool editParts = _editParts.IsChecked == true;
        _refill.Visibility = editParts ? Visibility.Visible : Visibility.Collapsed;
        _auto.Visibility = editParts ? Visibility.Collapsed : Visibility.Visible;
        if (editParts)
            _partRows.Children.Add(Note("차례가 산출서에 적는 차례입니다."));
        foreach (var group in _editor.Parts.GroupBy(p => PartCatalog.GroupIndex(p.Group)).OrderBy(g => g.Key))
        {
            string label = PartCatalog.Groups[group.Key].Label;
            if (editParts)
            {
                _partRows.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 2) });
                foreach (var part in group)
                {
                    string name = part.Name;
                    if (_asking == ("part", name)) { _partRows.Children.Add(AskRow("part", name)); continue; }
                    var plain = _ui.Check("무근", part.Plain, "켜면 이 부위를 산출서의 '무근 콘크리트'에 적습니다. 꺼져 있어도 유형·재료·부위 이름에 '무근'·'버림'이 있으면 무근입니다.", "plain.part." + name);
                    plain.Margin = new Thickness(8, 0, 0, 0);
                    plain.Click += (_, _) => Do(() => _editor.SetPlain(name, plain.IsChecked == true));
                    var noZone = _ui.Check("중분류 생략", part.NoZone, "켜면 산출서에서 이 부위 아래에 중분류 작은 제목(«- 배관실»)을 달지 않고 한데 적습니다 — '계단'·'밸브 받침'처럼. 끄면 중분류가 둘 이상일 때 작은 제목으로 나눕니다.", "nozone.part." + name);
                    noZone.Margin = new Thickness(8, 0, 0, 0);
                    noZone.Click += (_, _) => Do(() => _editor.SetNoZone(name, noZone.IsChecked == true));
                    var later = _ui.Check("나중에 침", PartCatalog.IsLater(part), "거푸집: 켜면 벽·슬래브 같은 구조체에 맞닿은 면을 이 부위에서만 뺍니다 — 구조체 쪽은 그 자리까지 셉니다(받침·PAD·디딤판처럼 나중에 붙여 치는 것). 겹친 콘크리트를 어느 쪽에서 뺄지는 «겹친 자리»가 정합니다.", "later.part." + name);
                    later.Margin = new Thickness(8, 0, 0, 0);
                    later.Click += (_, _) => Do(() => _editor.SetLater(name, later.IsChecked == true));
                    // 좁은 판에서는 «겹친 자리»가 다음 줄로 넘어간다(잘리지 않게 — R54).
                    var checks = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    checks.Children.Add(plain);
                    checks.Children.Add(noZone);
                    checks.Children.Add(later);
                    var rank = RankChoice(part);
                    rank.Margin = new Thickness(8, 3, 0, 0);
                    checks.Children.Add(rank);
                    // 셋째 줄(R49): 레미콘 규격 · 타설 — 비어 있으면 처음 값이 흐리게 보인다(2D 절 제목 «( 철근 , 25 - 30 - 15 ) : 펌프차 붐타설»).
                    var marks = new StackPanel();
                    marks.Children.Add(checks);
                    marks.Children.Add(SpecLine(part));
                    _partRows.Children.Add(EditRow("part." + name, name, null, marks,
                        _editor.CanMovePart(name, -1), _editor.CanMovePart(name, 1), step => Do(() => _editor.MovePart(name, step)), () => AskDelete("part", name),
                        text => Do(() => _editor.RenamePart(name, text))));
                }
                continue;
            }
            var line = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, Margin = new Thickness(0, 7, 6, 0), VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis });
            var buttons = new UniformGrid { Columns = 2, Opacity = any ? 1 : 0.6 };
            foreach (var part in group)
            {
                string name = part.Name;
                // 유형 이름이 다른 부위 묶음을 가리키는 부재(R57 P4 — 경고만): 단추에 ⚠ · 풍선 글에 부재.
                var odd = _editor.Members.Where(m => m.Value.Part == name && FormworkPartCheck.NameMismatch(m.Type, name, part.Group) != null).ToList();
                string tip = any ? $"고른 부재 {picked.Count}개의 부위를 '{name}'(으)로 넣습니다." : "먼저 그림에서 부재를 고르세요.";
                if (odd.Count > 0)
                    tip += $"\n⚠ 유형 이름이 다른 부위를 가리키는 부재 {odd.Count}개 — " + string.Join(", ", odd.Take(4).Select(m => $"«{m.Type}» Id {m.Id}")) + (odd.Count > 4 ? " …" : "");
                buttons.Children.Add(PickButton("row.part." + name, null, odd.Count > 0 ? name + " ⚠" : name, StateOf(m => m.Value.Part == name), tip,
                    () => Do(() => _editor.SetPart(name))));
            }
            Grid.SetColumn(buttons, 1);
            line.Children.Add(buttons);
            _partRows.Children.Add(line);
        }
    }

    // 부위 목록 고치기의 «겹친 자리»(R54 — 겹친 자리 주인표): 다른 부재와 겹친 자리를 누가 가지는지(숫자가 작은 쪽). 버림 줄은 늘 1 이라 막아 둔다.
    private FrameworkElement RankChoice(ZonePart part)
    {
        string name = part.Name;
        int now = PartCatalog.RankOf(part), first = DeductRank.BaseOf(part);
        var combo = _ui.Combo($"다른 부재와 겹친 자리를 누가 가지는지 — 숫자가 작은 쪽이 가지고 큰 쪽에서 뺍니다(모델 검증 · 겹침 결합 · 거푸집이 이 순위를 씁니다). 처음 값: {DeductRank.Label(first)}.",
                              "rank.part." + name);
        foreach (var row in DeductRank.Rows) combo.Items.Add(DeductRank.Label(row.Rank));
        combo.SelectedIndex = now - 1;
        combo.Width = 136;          // 가장 긴 «9 붙여 치는 것»이 다 보이게
        combo.MinHeight = 24;
        combo.IsEnabled = !DeductRank.IsLeanPart(part);
        // 닫혀 있는 칸 위에서 굴려도 바뀌지 않게(바뀔 때마다 모델에 목록을 적는다 — 구조물 칸과 같다).
        combo.PreviewMouseWheel += (_, e) => { if (!combo.IsDropDownOpen) e.Handled = true; };
        combo.SelectionChanged += (_, _) =>
        {
            int want = combo.SelectedIndex + 1;
            if (combo.SelectedIndex < 0 || want == now) return;
            _rankChanged = true;        // 열 때 한 빠른 검사의 기준이 달라졌다 — 딱지에 알린다(revit-verify R54 설치 전 M3)
            Do(() => _editor.SetRank(name, want));
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock
        {
            Text = "겹친 자리", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            Foreground = now != first ? _ui["AccentText"] : _sub,      // 처음 값과 다르면 눈에 띄게
        });
        line.Children.Add(combo);
        return line;
    }

    // 부위 목록 고치기의 셋째 줄: «규격 [25-30-15] 타설 [펌프차 붐타설]» — Enter 나 칸을 떠날 때 적는다(빈 칸 = 처음 값).
    private FrameworkElement SpecLine(ZonePart part)
    {
        string name = part.Name;
        bool plain = part.Plain || CalcPlan.WorkOf("", "", name) == CalcPlan.Plain;
        var spec = _ui.Input(PartCatalog.DefaultSpec(name, plain, part.Group), "산출근거 절 제목의 레미콘 규격(굵은골재-강도-슬럼프). 비우면 처음 값(흐린 글).", "spec.part." + name);
        var pour = _ui.Input(PartCatalog.DefaultPour, "절 제목 끝의 타설 방법. 비우면 처음 값(흐린 글).", "pour.part." + name);
        spec.Text = part.Spec ?? "";
        pour.Text = part.Pour ?? "";
        spec.Width = 92;
        pour.Width = 120;
        spec.MinHeight = pour.MinHeight = 24;
        bool sent = false;
        // 규격이 숫자-숫자-숫자가 아니면 빨간 칸 · 적지 않음(revit-verify M5).
        bool Bad() => spec.Text.Trim().Length > 0 && PartCatalog.NormalizeSpec(spec.Text) == null;
        void Look()
        {
            bool bad = Bad();
            spec.BorderBrush = bad ? _ui["BadFg"] : _ui["InputLine"];
            spec.Background = bad ? _ui["BadBg"] : _ui["InputBg"];
        }
        spec.TextChanged += (_, _) => Look();
        void Commit()
        {
            // 틀린 규격은 적지 않고(칸은 빨갛게 남음) 타설만 적는다(revit-verify R49 L2).
            string? tidy = Bad() ? part.Spec : PartCatalog.NormalizeSpec(spec.Text);
            if (sent || (tidy == part.Spec && (pour.Text.Trim().Length == 0 ? null : pour.Text.Trim()) == part.Pour)) return;
            sent = true;   // 적으면 목록이 다시 그려져 이 칸이 사라진다(이름 칸과 같은 까닭 — 한 번만)
            Do(() => _editor.SetSpec(name, tidy ?? "", pour.Text));
        }
        void Enter()
        {
            Commit();
            // 빨간 테두리는 글을 치는 동안 칸의 초점 테두리에 가려진다 — Enter 에 짧은 알림(revit-verify R49 L1).
            if (Bad()) ShowMessage("규격은 «25-30-15» 꼴(숫자-숫자-숫자)로 적습니다.", warn: true);
        }
        foreach (var box in new[] { spec, pour })
        {
            box.KeyDown += (_, e) =>
            {
                // 한글을 조합하는 중의 Enter 는 ImeProcessed 로 온다 — 조합이 끝난 뒤(글이 칸에 들어간 뒤) 적는다. 그사이 창이 닫혔으면 적지 않는다.
                if (e.Key == Key.Enter) { Enter(); e.Handled = true; }
                else if (e.Key == Key.ImeProcessed && e.ImeProcessedKey == Key.Enter)
                {
                    box.Dispatcher.BeginInvoke(new Action(() => { if (box.IsLoaded) Enter(); }), System.Windows.Threading.DispatcherPriority.Input);
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => Commit();
        }
        _dropPending.Add(() => sent = true);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 3, 0, 0) };
        line.Children.Add(new TextBlock { Text = "규격", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        line.Children.Add(spec);
        line.Children.Add(new TextBlock { Text = "타설", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
        line.Children.Add(pour);
        return line;
    }

    // 고른 부재에 넣는 단추: [색] 이름 — 고른 부재가 모두 이 값이면 켜져 보이고(state = "on"), 일부만이면 테두리만("some").
    private Button PickButton(string id, FrameworkElement? chip, string name, string state, string tip, Action apply, bool muted = false)
    {
        var content = new DockPanel { LastChildFill = true };
        if (chip != null) { DockPanel.SetDock(chip, Dock.Left); content.Children.Add(chip); }
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (muted && state != "on") label.Foreground = _sub;
        content.Children.Add(label);
        var button = new Button { Style = _ui.StyleOf("BtnPick"), Content = content, Tag = state, Margin = new Thickness(0, 0, 4, 4) };
        _ui.Mark(button, tip, id);
        button.Click += (_, _) => apply();
        return button;
    }

    // 중분류 입력칸의 목록: 이미 만든 이름(3D 의 색 · 부재 수) → 구조물의 추천 이름(아직 목록에 없는 것). 고르면 바로 넣는다(PutPlace).
    // 펼쳐 있는 동안에는 다시 짓지 않는다(고르던 줄이 사라지지 않게). 적던 글은 그대로 둔다.
    private void RefreshPlaceChoices()
    {
        if (_place.IsDropDownOpen) return;
        string typed = _place.Text;
        _syncing = true;
        try
        {
            _place.Items.Clear();
            for (int i = 0; i < _editor.Places.Count; i++)
            {
                string name = _editor.Places[i].Name;
                var c = ZonePalette.Of(i);
                _place.Items.Add(Choice("pick.place." + name, name, Swatch(c.r, c.g, c.b, 8), _editor.CountInPlace(name).ToString(CultureInfo.InvariantCulture)));
            }
            foreach (string name in _editor.SuggestedPlaces)
            {
                var plus = UiTheme.Icon(UiTheme.IconAdd, 10);
                plus.Foreground = _sub;
                plus.Margin = new Thickness(1, 1, 9, 0);
                _place.Items.Add(Choice("suggest.place." + name, name, plus, "추천"));
            }
            _place.SelectedIndex = -1;
            if (_place.Text != typed) _place.Text = typed;
        }
        finally { _syncing = false; }
    }

    // 목록의 한 줄: [색 또는 +] 이름 …… 부재 수 또는 '추천'. 마우스로 고르면 바로 넣는다(화살표로 고르면 글 칸에만 들어간다 — Enter 로 넣는다).
    private ComboBoxItem Choice(string id, string name, FrameworkElement chip, string note)
    {
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chip, Dock.Left);
        line.Children.Add(chip);
        var tail = new TextBlock { Text = note, Foreground = _sub, FontSize = 11.5, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tail, Dock.Right);
        line.Children.Add(tail);
        line.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Content = line };
        TextSearch.SetText(item, name);      // 고르면 글 칸에 이 이름이 들어간다
        _ui.Mark(item, "", id);
        item.PreviewMouseLeftButtonUp += (_, _) => _place.Dispatcher.BeginInvoke(() => PutPlace(name));
        return item;
    }

    // 목록을 고치는 줄: [색][이름 칸] [체크 칸들] [▲][▼][✕]. 이름을 고치고 Enter — 그 값인 부재를 모두 고쳐 적는다.
    private FrameworkElement EditRow(string id, string name, FrameworkElement? chip, FrameworkElement? checks,
                                     bool canUp, bool canDown, Action<int> move, Action remove, Action<string> rename)
    {
        var box = _ui.Input("", "이름을 고치고 Enter — 이 값인 부재를 모두 고쳐 적습니다. 이미 있는 이름이면 그쪽으로 합칩니다.", "name." + id);
        box.Text = name;
        box.MinHeight = 26;
        // 한 번만 적는다: Enter 로 적으면 목록이 다시 그려지며 이 칸이 사라지고, 그때 '글자판을 잃음'이 한 번 더 온다.
        bool sent = false;
        void Commit()
        {
            if (sent || box.Text.Trim() == name) return;
            sent = true;
            rename(box.Text);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        box.LostFocus += (_, _) => Commit();
        _dropPending.Add(() => sent = true);
        // ✕ 를 누르면 이 줄의 적다 만 이름은 버린다. 도구 단추는 글자판을 가져가지 않으므로, 버리지 않으면 줄이 '지울까요?'로 바뀌어
        // 이름 칸이 사라질 때 뒤늦게 이름 고치기가 모델에 적히고 묻던 줄까지 접힌다(2026-10-13 검토 R26 — 읽어서 찾음).
        var tools = EditTools(id, canUp, canDown, move, () =>
        {
            sent = true;
            remove();
        });
        var line = new DockPanel { LastChildFill = true };
        if (chip != null) { DockPanel.SetDock(chip, Dock.Left); line.Children.Add(chip); }
        DockPanel.SetDock(tools, Dock.Right);
        line.Children.Add(tools);
        line.Children.Add(box);
        if (checks == null) return new Border { Child = line, Padding = new Thickness(0, 1, 0, 1), MinHeight = 28 };
        // 표시 칸(무근 · 중분류 생략 · 나중에 침)은 이름 아래 줄에 둔다 — 한 줄에 다 넣으면 이름 칸에 두세 글자만 보인다(2026-10-15 미리보기 그림).
        var rows = new StackPanel();
        rows.Children.Add(line);
        checks.Margin = new Thickness(0, 2, 0, 0);
        rows.Children.Add(checks);
        return new Border { Child = rows, Padding = new Thickness(0, 1, 0, 4), MinHeight = 28 };
    }

    private FrameworkElement EditTools(string id, bool canUp, bool canDown, Action<int> move, Action remove)
    {
        Button Tool(string glyph, string tip, string name, bool enabled = true)
        {
            var button = new Button { Style = _ui.StyleOf("BtnGhost"), Content = UiTheme.Icon(glyph, 10), Width = 24, MinHeight = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0), IsEnabled = enabled, Focusable = false };
            _ui.Mark(button, tip, name + "." + id);
            return button;
        }
        var up = Tool(UiTheme.IconUp, "앞으로 옮깁니다(묶음의 맨 앞이면 앞 묶음으로).", "up", canUp);
        var down = Tool(UiTheme.IconDown, "뒤로 옮깁니다(묶음의 맨 뒤면 뒤 묶음으로).", "down", canDown);
        var delete = Tool(UiTheme.IconClose, "목록에서 지웁니다. 쓰는 부재가 있으면 먼저 묻습니다 — 지우면 그 부재들의 이 칸도 비워집니다.", "delete");
        up.Margin = new Thickness(6, 0, 0, 0);
        up.Click += (_, _) => move(-1);
        down.Click += (_, _) => move(1);
        delete.Click += (_, _) => remove();
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(up);
        line.Children.Add(down);
        line.Children.Add(delete);
        return line;
    }

    // ✕: 쓰는 부재가 없으면 바로 지우고, 있으면 그 줄을 '지울까요?' 줄로 바꾼다(JACK 2026-10-13 — 아무 일도 안 일어나면 고장으로 보인다).
    private void AskDelete(string kind, string name)
    {
        // 다른 줄의 적다 만 칸도 버린다 — 줄이 '지울까요?'로 바뀌며 그 칸이 사라질 때 뒤늦게 적히면 묻던 줄이 접힌다(revit-verify R49 M1).
        DropPending();
        var users = kind == "place" ? _editor.UsersOfPlace(name) : _editor.UsersOfPart(name);
        if (users.Total == 0)
        {
            Do(() => kind == "place" ? _editor.RemovePlace(name) : _editor.RemovePart(name));
            return;
        }
        _asking = (kind, name);
        _keepAsking = true;
        if (kind == "place" && _partsBlock != null) _partsHold = _partsBlock.ActualHeight;
        RefreshAll();
    }

    // '지울까요?' 줄: 쓰는 부재가 몇 개인지(안 보이는 것 · 고칠 수 없는 것도) 알리고 [지우기] · [취소].
    private FrameworkElement AskRow(string kind, string name)
    {
        var users = kind == "place" ? _editor.UsersOfPlace(name) : _editor.UsersOfPart(name);
        string what = kind == "place" ? "중분류" : "부위";
        bool stuck = users.Locked == users.Total;
        string text = stuck
            ? $"'{name}' — 쓰는 부재 {users.Total}개가 모두 고칠 수 없는 부재라 지울 수 없습니다."
            : $"'{name}' — 부재 {users.Total}개가 쓰고 있습니다" + (users.Unseen > 0 ? $"(지금 안 보이는 {users.Unseen}개 포함)" : "") +
              $". 지우면 그 부재들의 {what}도 비워집니다" +
              (users.Locked > 0 ? $" — 고칠 수 없는 {users.Locked}개는 값이 남아, 이름도 목록에 남습니다." : ".");
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = text, Foreground = _warn, TextWrapping = TextWrapping.Wrap, LineHeight = 18, FontSize = 12.5 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (!stuck)
        {
            var yes = _ui.Button("지우기", "BtnDanger", $"'{name}'을(를) 목록에서 지우고, 그 부재들의 {what}를 비웁니다. [되돌리기](Ctrl+Z) 한 번으로 돌아옵니다.", $"confirm.delete.{kind}.{name}");
            yes.MinHeight = 26;
            yes.Padding = new Thickness(12, 0, 12, 1);
            yes.Margin = new Thickness(0, 0, 6, 0);
            yes.Focusable = false;
            yes.Click += (_, _) => Do(() => kind == "place" ? _editor.RemovePlace(name, clear: true) : _editor.RemovePart(name, clear: true));
            buttons.Children.Add(yes);
        }
        var no = _ui.Button(stuck ? "알겠습니다" : "취소", "BtnSecondary", "그대로 둡니다.", $"cancel.delete.{kind}.{name}");
        no.MinHeight = 26;
        no.Padding = new Thickness(12, 0, 12, 1);
        no.Focusable = false;
        no.Click += (_, _) => RefreshAll();        // 묻던 것을 접는다(RefreshAll 이 지운다)
        buttons.Children.Add(no);
        box.Children.Add(buttons);
        var row = new Border { Background = _ui["WarnBg"], CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 2, 0, 3), Child = box };
        _ui.Mark(row, "", $"ask.delete.{kind}.{name}");
        return row;
    }

    private TextBlock Note(string text) => new() { Text = text, Foreground = _sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 3) };

    // ── 색 · 글 ──────────────────────────────────────────────────────────────────

    /// <summary>부재의 색: 아직 = 옅은 회색 · 끝 = 중분류 색('단독객체'는 자주) · 덜 끝남 = 그 색을 옅게(중분류가 없으면 크림색) · 고칠 수 없음 = 짙은 회색.</summary>
    public (byte r, byte g, byte b) ColorOf(PickMember m)
    {
        static (byte, byte, byte) Rgb((string name, byte r, byte g, byte b) c) => (c.r, c.g, c.b);
        var state = m.State;
        if (state == PickState.Locked) return Rgb(ZonePalette.Locked);
        if (state == PickState.Todo) return Rgb(ZonePalette.Todo);
        (string name, byte r, byte g, byte b)? place = null;
        if (m.Value.Zone.Length > 0)
        {
            int at = _editor.PlaceIndex(m.Value.Zone);
            place = ZonePalette.Of(at < 0 ? 0 : at);
        }
        else if (m.Value.Free) place = ZonePalette.NoPlace;
        if (state == PickState.Done && place is { } done) return Rgb(done);
        return place is { } some ? ZonePalette.Pale(some) : Rgb(ZonePalette.PartOnly);
    }

    /// <summary>부재 하나를 한 줄로(마우스 밑의 부재).</summary>
    public string Describe(PickMember m)
    {
        string zone = m.Value.Zone.Length > 0 ? m.Value.Zone : m.Value.Free ? ZonePlan.NoPlaceLabel : "(없음)";
        string part = m.Value.Part.Length > 0 ? m.Value.Part : "(없음)";
        string state = m.State switch
        {
            PickState.Done => "끝",
            PickState.Todo => "아직",
            PickState.Partial => "하나만 넣음",
            PickState.Outside when m.Value.Changed && !m.Value.ZoneOutside && !m.Value.PartOutside => "넣은 뒤 모양이 바뀜(부피·크기·자리) — 확인 필요",
            PickState.Outside => (m.Value.ZoneOutside && m.Value.PartOutside ? "중분류·부위" : m.Value.ZoneOutside ? "중분류" : "부위") + "가 창 밖에서 들어온 값 — 확인 필요" +
                                 (m.Value.Changed ? " · 모양도 바뀜" : ""),
            _ => "고칠 수 없음(" + m.Locked + ")",
        };
        return $"{m.Category} {m.Type} · Id {m.Id} · 높이 {Signed(m.ZMin + _shift)} ~ {Signed(m.ZMax + _shift)} · 중분류: {zone} · 부위: {part} · {state}";
    }

    private static string Signed(double v) => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    // 고른 부재 띠: 몇 개 · 무슨 부재 · 지금 들어 있는 값.
    private void RefreshPicked()
    {
        var picked = _editor.Selected.ToList();
        bool any = picked.Count > 0;
        _unpick.IsEnabled = _sameType.IsEnabled = _clear.IsEnabled = _auto.IsEnabled = any;
        int outside = picked.Count(m => m.State == PickState.Outside);
        _confirm.Visibility = outside > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pickedHead.Inlines.Clear();
        _pickedValues.Children.Clear();
        _pickedHead.Inlines.Add(new Run("고른 부재  ") { Foreground = _sub });
        if (!any)
        {
            _pickedHead.Inlines.Add(new Run("없음") { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = _sub });
            _pickedHint.Text = "찍으면 더해지고 다시 찍으면 빠집니다 · 끌면 네모 안의 부재가 한꺼번에(왼→오: 다 든 것만 · 오→왼: 걸친 것도)";
            _pickedHint.Visibility = Visibility.Visible;
            _pickedText = "부재를 찍으면 더해지고, 다시 찍으면 빠집니다.\n끌면 네모 안의 부재가 한꺼번에 — 왼→오: 다 든 것만 · 오→왼: 걸친 것도";
            return;
        }
        _pickedHint.Visibility = Visibility.Collapsed;
        static List<(string key, int n)> Groups(IEnumerable<string> keys) =>
            keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Count())).ToList();
        static string Join(List<(string key, int n)> groups) => string.Join(" · ", groups.Take(4).Select(g => $"{g.key} {g.n}")) + (groups.Count > 4 ? $" · 그 밖 {groups.Count - 4}가지" : "");
        var kinds = Groups(picked.Select(m => $"{m.Category} {m.Type}"));
        var zones = Groups(picked.Select(m => m.Value.Zone.Length > 0 ? m.Value.Zone : m.Value.Free ? ZonePlan.NoPlaceLabel : "(없음)"));
        var parts = Groups(picked.Select(m => m.Value.Part.Length > 0 ? m.Value.Part : "(없음)"));
        _pickedHead.Inlines.Add(new Run($"{picked.Count}개") { FontSize = 15.5, FontWeight = FontWeights.SemiBold });
        _pickedHead.Inlines.Add(new Run("     " + Join(kinds)));

        // 지금 값: 중분류 [수조부 2][안 넣음 1]   부위 [벽체 3]
        void Values(string label, List<(string key, int n)> groups, bool first)
        {
            _pickedValues.Children.Add(new TextBlock { Text = label, Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(first ? 0 : 14, 0, 6, 0) });
            foreach (var (key, n) in groups.Take(4))
            {
                bool empty = key == "(없음)";
                var text = new TextBlock { FontSize = 12, Foreground = empty ? _sub : _text };
                text.Inlines.Add(new Run(empty ? "안 넣음" : key));
                if (picked.Count > 1) text.Inlines.Add(new Run("  " + n.ToString(CultureInfo.InvariantCulture)) { FontWeight = FontWeights.SemiBold });
                var pill = _ui.Pill(text, empty ? "NeutralBg" : "AccentSoft");
                pill.Margin = new Thickness(0, 0, 5, 0);
                _pickedValues.Children.Add(pill);
            }
            if (groups.Count > 4) _pickedValues.Children.Add(new TextBlock { Text = $"그 밖 {groups.Count - 4}가지", Foreground = _sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }
        Values("부위", parts, true);
        Values("중분류", zones, false);
        if (outside > 0)
            _pickedValues.Children.Add(new TextBlock { Text = $"창 밖에서 들어온 값 {outside}개 — 맞으면 [이 값 그대로 확인]", Foreground = _warn, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        _pickedText = $"부재 {picked.Count}개 — {Join(kinds)}\n부위: {Join(parts)}   |   중분류: {Join(zones)}" + (outside > 0 ? $"\n창 밖에서 들어온 값 {outside}개 — 맞으면 [이 값 그대로 확인]" : "");
    }

    private void RefreshProgress()
    {
        var p = _editor.Progress();
        _remain.Text = p.Remaining == 0 ? "남은 부재 없음 — 모두 끝" : $"남은 부재 {p.Remaining}개";
        _remain.Foreground = p.Remaining == 0 ? _ui["Good"] : _text;
        _total.Text = $"전체 {p.Total}";

        // 나눠 센 수: 이름은 흐리게, 수는 또렷하게.
        _counts.Inlines.Clear();
        void Count(string label, int n, Brush? color = null)
        {
            if (_counts.Inlines.Count > 0) _counts.Inlines.Add(new Run("   "));
            _counts.Inlines.Add(new Run(label + " "));
            _counts.Inlines.Add(new Run(n.ToString(CultureInfo.InvariantCulture)) { Foreground = color ?? _text, FontWeight = FontWeights.SemiBold });
        }
        Count("끝", p.Done);
        Count("하나만", p.Partial);
        Count("아직", p.Todo);
        if (p.Outside > 0) Count("확인 필요", p.Outside, _warn);
        _counts.Inlines.Add(new Run("   ·   딱지를 누르면 그 부재를 고릅니다") { FontSize = 11.5 });
        string notes = (p.Locked > 0 ? $"고칠 수 없는 부재 {p.Locked}개(그룹 안 · 다른 사용자 · 속성 칸 없음)는 세지 않았습니다." : "") +
                       (p.Locked > 0 && p.Undrawn > 0 ? "\n" : "") +
                       (p.Undrawn > 0 ? $"형상이 없어 그리지 못한 부재 {p.Undrawn}개는 이 창에서 정할 수 없습니다." : "");
        _countNotes.Text = notes;
        _countNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _countsText = $"전체 {p.Total} · 끝 {p.Done} · 아직 {p.Todo} · 하나만 {p.Partial}" + (p.Outside > 0 ? $" · 확인 필요 {p.Outside}" : "") + (notes.Length > 0 ? "\n" + notes : "");

        int doable = p.Total - p.Locked;
        double done = doable > 0 ? Math.Clamp((double)p.Done / doable, 0, 1) : 1;
        _barDone.Width = new GridLength(done, GridUnitType.Star);
        _barRest.Width = new GridLength(1 - done, GridUnitType.Star);
        _undo.IsEnabled = _editor.CanUndo;
        _ui.Tip(_undo, _editor.CanUndo ? $"방금 한 일을 되돌립니다(Ctrl+Z): {_editor.UndoLabel}" : "되돌릴 것이 없습니다.");
        int hidden = _editor.Hidden.Count;
        _status.Text = "넣는 즉시 모델에 적힙니다(창을 닫아도 남습니다)" + (_editor.Writes > 0 ? $" · 이 창에서 적은 횟수 {_editor.Writes}" : "") + (hidden > 0 ? $" · 숨긴 부재 {hidden}개" : "");
    }

    // 지금 적용 상태: 중분류별(그림의 색) · 부위별 부재 수. 딱지를 누르면 그 값인 부재를 고르기만 한다(넣지 않는다).
    private void RefreshState()
    {
        Button Chip(string id, FrameworkElement? swatch, string name, int count, string tip, Func<PickMember, bool> match, string what, string background = "BtnChip")
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (swatch != null) content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontWeight = FontWeights.SemiBold, Foreground = count > 0 ? _text : _sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            var chip = new Button { Style = _ui.StyleOf(background), Content = content, MinHeight = 23, FontSize = 12, Padding = new Thickness(8, 0, 9, 1), Margin = new Thickness(0, 0, 5, 4), Focusable = false, Cursor = Cursors.Hand };
            _ui.Mark(chip, tip, id);
            chip.Click += (_, _) => { _editor.SelectWhere(match, what); RefreshAll(); };
            return chip;
        }

        _statusPlaces.Children.Clear();
        for (int i = 0; i < _editor.Places.Count; i++)
        {
            string name = _editor.Places[i].Name;
            var c = ZonePalette.Of(i);
            _statusPlaces.Children.Add(Chip("status.place." + name, Swatch(c.r, c.g, c.b, 6), name, _editor.CountInPlace(name),
                $"중분류가 '{name}'인 부재를 고릅니다(고르기만 합니다).", m => m.Value.Zone == name, $"중분류 '{name}'"));
        }
        int free = _editor.CountNoPlace;
        if (free > 0)
            _statusPlaces.Children.Add(Chip("status.place.none", Swatch(ZonePalette.NoPlace.r, ZonePalette.NoPlace.g, ZonePalette.NoPlace.b, 6), ZonePlan.NoPlaceLabel, free,
                $"'{ZonePlan.NoPlaceLabel}'(으)로 정한 부재를 고릅니다.", m => m.Value.Free, $"'{ZonePlan.NoPlaceLabel}'"));
        int noPlace = _members.Count(m => m.Drawn && !m.Value.HasZone);
        _statusPlaces.Children.Add(Chip("status.place.todo", Swatch(ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b, 6), "안 넣음", noPlace,
            "중분류를 아직 넣지 않은 부재를 고릅니다.", m => m.Drawn && !m.Value.HasZone, "중분류를 안 넣은 부재"));

        _statusParts.Children.Clear();
        foreach (var part in _editor.Parts)
        {
            int count = _editor.CountOfPart(part.Name);
            if (count == 0) continue;
            string name = part.Name;
            _statusParts.Children.Add(Chip("status.part." + name, null, name, count, $"부위가 '{name}'인 부재를 고릅니다(고르기만 합니다).", m => m.Value.Part == name, $"부위 '{name}'"));
        }
        int noPart = _members.Count(m => m.Drawn && !m.Value.HasPart);
        _statusParts.Children.Add(Chip("status.part.todo", null, "안 넣음", noPart, "부위를 아직 넣지 않은 부재를 고릅니다.", m => m.Drawn && !m.Value.HasPart, "부위를 안 넣은 부재"));

        _statusFlags.Children.Clear();
        var p = _editor.Progress();
        if (p.Outside > 0)
        {
            int changed = _editor.Members.Count(m => m.State == PickState.Outside && m.Value.Changed);
            _statusFlags.Children.Add(Chip("status.outside", null, "확인 필요", p.Outside,
                "창 밖에서 들어온 값(복사한 부재 · 속성 창에서 넣은 값)이 있거나, 넣은 뒤 모양(부피·크기·자리)이 바뀐 부재를 고릅니다." +
                (changed > 0 ? $"\n모양이 바뀐 부재 {changed}개 — 값이 맞으면 [확인], 아니면 고쳐 넣으세요." : ""),
                m => m.State == PickState.Outside, "확인이 필요한 부재"));
        }
        if (p.Locked > 0)
            _statusFlags.Children.Add(Chip("status.locked", Swatch(ZonePalette.Locked.r, ZonePalette.Locked.g, ZonePalette.Locked.b, 6), "고칠 수 없음", p.Locked,
                "고칠 수 없는 부재(그룹 안 · 다른 사용자 · 속성 칸 없음)를 고릅니다.", m => m.State == PickState.Locked, "고칠 수 없는 부재"));
        // 모델의 문제(R63b — 열 때 빠른 모델 검증 · ② 에서 본 것): 짧은 딱지 · 누르면 그 부재를 고른다(고치는 곳은 리본의 [모델 검증] · ① 의 부위).
        foreach (var issue in AllIssues()) _statusFlags.Children.Add(IssueChip(issue, new Thickness(0, 0, 5, 4)));
        // 검사를 못 했거나 끝까지 못 본 것 · 이 창에서 «겹친 자리»를 바꿈(기준이 달라짐 — 엑셀 메모는 [완료] 때 지금 순위로 다시 검사한다): 보이기만 하는 딱지.
        void NotePill(string say, string tip, string id)
        {
            var pill = _ui.Pill(new TextBlock { Text = say, FontSize = 12, Foreground = _warn, VerticalAlignment = VerticalAlignment.Center }, "WarnBg");
            pill.Margin = new Thickness(0, 0, 5, 4);
            _ui.Mark(pill, tip, id);
            _statusFlags.Children.Add(pill);
        }
        if (ModelCheck.Length > 0) NotePill(ModelCheck, "이 창을 열 때 한 빠른 모델 검증 — 리본의 [모델 검증]으로 확인하세요.", "status.modelCheck");
        if (_rankChanged) NotePill("겹친 자리 바꿈", "이 창에서 «겹친 자리»를 바꿨습니다 — 리본의 [모델 검증]으로 다시 확인하세요.", "status.rankChanged");
    }

    /// <summary>열 때 한 빠른 모델 검증에서 딱지(Issues)로 못 보인 글(검사 오류 · 끝까지 못 봄 — R63b · "" = 없음). 위 띠에 상태 딱지로 보인다.</summary>
    public string ModelCheck { get; set; } = "";
    private bool _rankChanged;      // 이 창에서 «겹친 자리»를 바꿨다

    /// <summary>그림 위에 알림을 띄운다("" = 지움). warn = 문제를 알리는 글(색을 달리한다).</summary>
    public void ShowMessage(string text, bool warn = false)
    {
        _message.Text = text;
        _message.Foreground = warn ? _warn : _text;
        _toast.Background = warn ? _ui["WarnBg"] : _ui["Glass"];
        _toast.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>편집 상태로 화면을 다시 맞춘다.</summary>
    public void RefreshAll()
    {
        // 지울지 묻던 줄 · [다음 단계]를 한 번 더 누르라는 알림은 다른 일을 하면 접는다(누른 바로 그 한 번만 남긴다).
        if (!_keepAsking)
        {
            _asking = null;
            _partsHold = 0;
        }
        _keepAsking = false;
        if (!_keepArmed) _nextArmed = false;
        _keepArmed = false;
        if (Step == 2)
        {
            RefreshReview();
            _view3d.Refresh();
            return;
        }
        if (Step == 1)
        {
            RefreshFormwork();
            _view3d.Refresh();
            return;
        }
        RefreshLists();
        RefreshPicked();
        RefreshProgress();
        RefreshState();
        _onlyRemaining.IsChecked = _editor.OnlyRemaining;
        ShowMessage(_editor.Message, _editor.Message.Contains("못했") || _editor.Message.Contains("오류") || _editor.Message.Contains("없습니다") || _editor.Message.Contains("먼저"));
        _view3d.Refresh();
    }

    /// <summary>
    /// 창에서 모델을 건드리는 일을 돌리는 길(R62): 기본은 그 자리에서 · 명령은 Revit 이 손이 빌 때 도는 길(ExternalEvent)을 넣는다.
    /// ① 의 일 하나하나가 Revit 실행 취소 목록의 한 줄(«DH 부위·중분류»).
    /// </summary>
    public IApiRunner Runner { get; set; } = DefaultRunner();

    /// <summary>새 창의 처음 길(시험이 «나중에 도는 길»로 바꾼다 — 출하 길을 타게 · R62 계획 검토 M2).</summary>
    public static Func<IApiRunner> DefaultRunner { get; set; } = () => ImmediateRunner.Instance;

    private const string EditUndo = "DH 부위·중분류";

    // 모델에 적는 일 하나: 넣을 때 바로 창을 잠그고(그동안 다시 눌리지 않게 — 적는 중에 Revit 의 창이 뜨면 우리 창이 눌릴 수 있다), 끝나면 풀고 화면을 맞춘다.
    // 바로 돌면 그 결과를, 나중에 돌면(Revit 이 다른 작업 중) true(넣음)를 돌려준다 — 결과에 따른 일은 then 에서.
    private bool Do(Func<bool> work, Action<bool>? then = null)
    {
        if (Busy) return false;
        if (Stale) { ShowMessage(StaleSay, true); return false; }
        _busy = true;
        Lock(true);                         // 3D 그림 · 목록 · 단추가 모두 눌리지 않는다(위 띠의 [취소]만 — 창의 X 는 닫기 처리기가 막는다)
        Root.Cursor = Cursors.Wait;         // 창 안만 — 기다리는 동안 Revit 쪽 마우스는 그대로(계획 검토)
        bool ok = false, finished = false;
        Runner.Run(EditUndo, () => ok = work(), error =>
        {
            Lock(false);
            Root.Cursor = null;
            _busy = false;
            finished = true;
            RefreshAll();
            if (error is OperationCanceledException) ShowMessage("넣지 않았습니다(취소).");
            else if (error != null) ShowMessage("오류가 났습니다: " + error.Message, true);
            then?.Invoke(ok && error == null);
        });
        return finished ? ok : true;
    }

    /// <summary>
    /// 바깥(명령)이 긴 일을 하는 중이다 — [완료]처럼 RunLocked 사이사이에 작은 창(산출식 확인 · 저장 위치 · 물음)이 뜨는 일 전체.
    /// 그 틈에 쌓여 있던 누르기가 [완료] · [다음 단계] · [이전 단계] · [닫기] · 되돌리기를 또 돌리지 않게 막는다(revit R42 중간 ①).
    /// RunLocked 는 이 표시와 상관없이 돈다(그 일 안에서 부르는 것이므로).
    /// </summary>
    public bool Held { get; set; }

    /// <summary>일지에 한 줄 적는다(명령이 건다 — ② 의 판 빼기 · 되살리기 · 다시 그린 시간).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>모델에 적는 중이거나 바깥이 긴 일을 하는 중인가(창을 닫으려 할 때 · 단추를 누를 때 본다).</summary>
    public bool Busy => _busy || Held;

    private int IndexOf(long id)
    {
        for (int i = 0; i < _members.Count; i++)
            if (_members[i].Id == id) return i;
        return -1;
    }

    // ── 시험에서 쓰는 길(화면에서 누르는 것과 같은 길) ───────────────────────────

    // 넣고(Do — 출하 길 그대로) 그 일이 끝날 때까지 디스패처를 돌린 뒤 결과(R62 — 나중에 도는 길에서도 시험이 결과를 보게).
    private bool DoAndWait(Func<bool> work)
    {
        bool? result = null;
        if (!Do(work, ok => result = ok)) return false;
        for (int i = 0; result == null && i < 1000; i++)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        return result == true;
    }

    /// <summary>중분류 목록에서 그 이름을 골랐다(고른 부재에 넣는다).</summary>
    public bool ClickPlace(string name) => DoAndWait(() => _editor.SetPlace(name));
    /// <summary>[단독객체]를 눌렀다.</summary>
    public bool ClickNoPlace() => DoAndWait(_editor.SetNoPlace);
    /// <summary>중분류 칸에 이름을 적고 Enter(또는 [넣기]) — 고른 것이 없고 새 이름이면 목록에만 보탠다.</summary>
    public void TypePlace(string text)
    {
        SetPlaceText(text);
        PutPlace(_place.Text);
    }
    /// <summary>중분류 칸에 지금 적힌 글.</summary>
    public string PlaceText => _place.Text;
    /// <summary>중분류 칸을 펼치면 나오는 이름(이미 만든 것 → 추천 차례).</summary>
    public IReadOnlyList<string> PlaceChoices => _place.Items.OfType<ComboBoxItem>().Select(i => TextSearch.GetText(i)).ToList();
    /// <summary>부위 단추를 눌렀다.</summary>
    public bool ClickPart(string name) => DoAndWait(() => _editor.SetPart(name));
    /// <summary>[되돌리기]를 눌렀다.</summary>
    public bool ClickUndo() => DoAndWait(_editor.Undo);
    public string RemainText => _remain.Text;
    public string CountsText => _countsText;
    public string PickedText => _pickedText;
    public string MessageText => _message.Text;
    public string StatusText => _status.Text;
    public string HoverText => _hover.Text;
    public IReadOnlyList<string> PlaceRowNames => _editor.Places.Select(p => p.Name).Append(ZonePlan.NoPlaceLabel).ToList();

    // ── 작은 부품 ────────────────────────────────────────────────────────────────

    private Button Small(string text, string tip, string id, string kind = "BtnSecondary")
    {
        var button = _ui.Button(text, kind, tip, id);
        button.Padding = new Thickness(10, 0, 10, 1);
        button.Margin = new Thickness(0, 0, 6, 0);
        return button;
    }

    // 목록의 머리: 이름 + 흐린 풀이 …… [더 있는 단추][목록 고치기]
    private DockPanel HeadLine(string text, string hint, ToggleButton toggle, Button? extra = null)
    {
        var line = new DockPanel { Margin = new Thickness(0, 9, 0, 6) };
        toggle.MinHeight = 26;
        toggle.FontSize = 12;
        toggle.Padding = new Thickness(9, 0, 9, 1);
        toggle.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(toggle, Dock.Right);
        line.Children.Add(toggle);
        if (extra != null)
        {
            extra.MinHeight = 26;
            extra.FontSize = 12;
            extra.Padding = new Thickness(9, 0, 9, 1);
            extra.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(extra, Dock.Right);
            line.Children.Add(extra);
        }
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.Inlines.Add(new Run(text) { FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        if (hint.Length > 0) label.Inlines.Add(new Run("   " + hint) { Foreground = _sub, FontSize = 12 });
        line.Children.Add(label);
        return line;
    }

    // [이름 칸][추가]: Enter 로도 넣는다.
    private DockPanel AddLine(TextBox box, string id, Action add)
    {
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var button = _ui.Button("추가", "BtnSoft", "", id, UiTheme.IconAdd);
        button.Margin = new Thickness(6, 0, 0, 0);
        button.Click += (_, _) => add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { add(); e.Handled = true; } };
        DockPanel.SetDock(button, Dock.Right);
        line.Children.Add(button);
        line.Children.Add(box);
        return line;
    }

    // 색 조각(둥근 네모). 옅은 색도 바탕과 가려지게 가는 테두리를 두른다.
    private Border Swatch(byte r, byte g, byte b, double right = 6) => new()
    {
        Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, right, 0),
    };

    private TextBlock Caption(string text, double left = 0) =>
        new() { Text = text, Foreground = _sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };

    private Border Rule() => new() { Height = 1, Background = _line };
}
