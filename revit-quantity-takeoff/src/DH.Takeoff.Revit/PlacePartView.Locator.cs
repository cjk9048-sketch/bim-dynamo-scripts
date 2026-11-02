using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

// R73a5 — «이 부재만 보기» 동안 3D 를 둘로(JACK 1102 «목록에서 선택할 때는 3D 뷰 팝업창 부분이 2분할로 돼서 한쪽은 윤곽선에 그 객체만 보이는 거, 하나는 지금 만드는 거 보이는 걸로 해 줘.
// 그리고 지금 있는 위치 인덱스 부분은 없어도 될 것 같아»):
//  · 왼쪽 «위치» = 모델 전체를 면 없는 윤곽(Ghost — 모서리만)으로 · 고른 부재만 강조색 몸(둘레는 면이 없어 가리지 않음) · 처음 = 오른쪽 3D 의 보는 방향으로 모델 전체 맞춤.
//    따로 돌리기 · 옮기기 · 확대(같은 손짓 · 가운데 두 번 = 모델 전체) · 찍기 · 고르기 · 도구 · 마우스 밑 글 없음(보기만 — 글자판도 가져가지 않음).
//  · 오른쪽 = 지금의 «이 부재만 보기» 그대로(빗금 · 딱지 · [빼기] · [넣기] · 띠). 이 부재만 보기가 끝나면 나눔을 걷고 한 장으로(오른쪽 3D 의 화면 1픽셀 길이는 그대로).
//  · 왼쪽 그림은 처음 나눌 때 한 번 짓고(부재 겉면 그대로 — ② 부재 겉면과 같은 MeshData) 고른 부재가 바뀌면 칠만 다시(Refresh). 3D 오른쪽 아래 평면 지도(R73a)는 걷음.
public sealed partial class PlacePartView
{
    private Model3DView? _locator;
    private Grid? _split;                                     // [위치 | 가름대 | 3D]
    private Grid? _locatorPane;
    private FrameworkElement? _splitter;
    private bool _locatorShown, _locatorFitPending;
    private string _locatorFor = "";
    // 처음 폭 = 45 : 55(내 판단 — 오른쪽 3D 의 도구 칸 + 보기 단추 · 띠가 한 줄에 들게 · 녹촌 1440 px 에서 650 | 790) · 가름대로 바꾼 폭은 다시 나눌 때 그대로.
    private GridLength _locatorWidth = new(0.82, GridUnitType.Star);
    private const double RightMinWidth = 480;                       // 오른쪽 3D 가장 좁은 폭(도구 칸 120 + 보기 단추 340 + 여백)

    /// <summary>시험 · 일지용: 왼쪽 «위치» 그림을 지은 ms(처음 한 번) · 마지막으로 맞춘 ms.</summary>
    public long LocatorBuildMilliseconds { get; private set; }

    // 3D 자리 = [위치(처음엔 폭 0 · 숨김) | 가름대 | 지금의 3D 무대].
    private FrameworkElement BuildSplit(FrameworkElement stage)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });   // 나눌 때만 RightMinWidth
        var pane = new Grid { Visibility = Visibility.Collapsed, ClipToBounds = true };
        var title = new Border
        {
            Background = _ui["Glass"], BorderBrush = _line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = true,
            Child = new TextBlock { Text = "위치", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = _text },
        };
        _ui.Mark(title, "건물 안에서 고른 부재의 자리 — 돌리기 · 옮기기 · 확대만 됩니다(찍기 없음 · 가운데 단추 두 번 = 모델 전체).", "fw.locator.title");
        pane.Children.Add(title);
        Grid.SetColumn(pane, 0);
        _ui.Mark(pane, "", "fw.locator");
        var splitter = new GridSplitter
        {
            Width = 6, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns, Background = _line, Focusable = false,
            Visibility = Visibility.Collapsed, ShowsPreview = false, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _ui.Mark(splitter, "끌어 두 그림의 폭을 바꿉니다.", "fw.locator.split");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(stage, 2);
        grid.Children.Add(pane);
        grid.Children.Add(splitter);
        grid.Children.Add(stage);
        _split = grid;
        _locatorPane = pane;
        _splitter = splitter;
        return grid;
    }

    // 나눔을 지금 것으로(RefreshIsoBar — 이 부재만 보기가 켜지고 꺼질 때마다): 켜지면 왼쪽 그림을 (처음이면 지어) 보이고 고른 부재를 칠한다 · 꺼지면 걷는다.
    // 화면 맞춤은 배치가 끝난 뒤(FitLocatorNow — 나무 줄 고르기가 날아가기 앞에 부른다).
    private void ShowLocator(bool on)
    {
        if (_split == null || _locatorPane == null || _splitter == null) return;
        if (on != _locatorShown)
        {
            _locatorShown = on;
            if (!on) _locatorWidth = _split.ColumnDefinitions[0].Width;
            _split.ColumnDefinitions[0].Width = on ? _locatorWidth : new GridLength(0);
            _split.ColumnDefinitions[0].MinWidth = on ? 200 : 0;
            _split.ColumnDefinitions[2].MinWidth = on ? RightMinWidth : 240;
            _locatorPane.Visibility = _splitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                if (_locator == null) BuildLocator();
                // 오른쪽 3D 의 보는 방향으로(처음 맞춤 · 다시 나눌 때 맞춤 둘 다).
                _locator!.Camera.Yaw = _view3d.Camera.Yaw;
                _locator.Camera.Pitch = _view3d.Camera.Pitch;
                _locatorFitPending = true;
            }
            Log?.Invoke(on ? $"② 위치 그림 켬\t부재 {_memberMeshes.Count} · 처음 지음 {LocatorBuildMilliseconds} ms" : "② 위치 그림 끔");
        }
        if (!on || _locator == null) return;
        string key = string.Join(",", (_fwIso ?? new HashSet<long>()).Order()) + "|" + string.Join(",", _editor.Hidden.Order());
        if (key == _locatorFor) return;
        _locatorFor = key;
        _locator.Refresh();
    }

    // 왼쪽 «위치» 그림(처음 한 번): ② 부재 겉면 그대로 · 고른 부재 = 맨 위 층 강조색 · 나머지 = 면 없는 윤곽 · 찍히지 않음 · 글자판을 가져가지 않음.
    private void BuildLocator()
    {
        var watch = Stopwatch.StartNew();
        var accent = ((SolidColorBrush)_ui["Accent"]).Color;
        var tint = (accent.R, accent.G, accent.B);
        var view = new Model3DView(_memberMeshes, _ui.IsDark, layered: true)
        {
            Shown = i => i < MemberCount && !_editor.Hidden.Contains(_members[i].Id),
            ColorOf = _ => tint,
            Picked = _ => false,
            // 고른 부재 = 보통(면 + 모서리 — 둘레는 면 없는 윤곽이라 가리지 않는다 · 맨 위 층을 쓰지 않아 빈 층을 그리지 않는다) · 나머지 = 윤곽.
            StyleOf = i => i < MemberCount && SoloShows(_members[i].Id) ? MeshStyle.Solid : MeshStyle.Ghost,
            Pickable = _ => false,
            GhostPickable = false,
            BandSelect = false,
            SeenFirst = false,
            Focusable = false,
            FlatGhosts = true,                     // 윤곽 = 화면 선(3D 모서리 띠보다 훨씬 가볍다 — 녹촌 한 장 80 → … ms)
        };
        _locator = view;
        _locatorPane!.Children.Insert(0, view);
        LocatorBuildMilliseconds = watch.ElapsedMilliseconds;
    }

    // 나눔이 막 켜졌으면 배치를 마치고(오른쪽 3D 의 폭이 바뀐다 — 날아갈 자리를 새 폭으로 센다) 왼쪽 그림을 모델 전체에 맞춘다.
    private void FitLocatorNow()
    {
        if (!_locatorFitPending || _locator == null || _split == null) return;
        _locatorFitPending = false;
        _split.UpdateLayout();
        var box = Box3.Empty;
        for (int i = 0; i < MemberCount; i++)
            if (_memberMeshes[i].Triangles > 0) box = box.With(_memberMeshes[i].Box);
        if (!box.IsEmpty) _locator.ZoomTo(box, 0.08, 0);   // 처음 맞춤(OrbitCamera.Fit 의 여백)과 같게 — 모서리 띠를 두 번 짓지 않게
    }

    // ── 시험에서 쓰는 길 ──

    /// <summary>R73a5: 왼쪽 «위치» 그림(나누지 않았으면 null · 지은 뒤 숨겼으면 숨긴 채) · 나뉘어 있는가 · 위치 칸 · 가름대.</summary>
    public Model3DView? Locator => _locator;
    public bool LocatorShown => _locatorShown;
    public FrameworkElement? LocatorPane => _locatorPane;
}
