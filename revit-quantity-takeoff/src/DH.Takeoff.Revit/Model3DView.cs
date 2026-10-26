using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using DH.Takeoff.Core.View3D;

namespace DH.Takeoff.Revit;

/// <summary>부재 하나를 그리는 방식 — 층을 나눠 그릴 때(Model3DView 의 layered)만 쓴다.</summary>
public enum MeshStyle
{
    /// <summary>면 + 모서리(「위치·부위 정하기」의 그리기).</summary>
    Solid,
    /// <summary>면 없이 모서리만 — 속이 다 비쳐 보이는 둘레 부재. 찍히지 않는다.</summary>
    Ghost,
    /// <summary>옅은 색 면(뒤가 비친다) + 제 색 모서리.</summary>
    Tint,
    /// <summary>맨 위 층에 불투명하게 — 다른 부재에 가려져 있어도 보이고, 찍을 때도 먼저 닿는다.</summary>
    Top,
}

/// <summary>
/// 3D 팝업의 그림 — 부재의 겉면(MeshData)을 직교 카메라로 그리고, 마우스로 돌리기·옮기기·확대와 찍기·끌어 고르기를 받는다.
/// Revit API 를 쓰지 않는다(화면만 따로 그려 볼 수 있게 — tools/uipreview). 무엇이 보이고 무슨 색인지는 바깥(편집기)이 정한다.
///  · 그리기: 부재마다 겉면 하나 + 모서리 띠 하나. 재료는 앞뒤 같은 색(세모의 도는 방향에 기대지 않는다). 반투명은 쓰지 않는다(그리는 차례 문제).
///  · 모서리: 3D 그림에는 선이 없어 면 테두리에 얇은 띠를 얹는다(Core 의 EdgeStrips). 폭이 화면 1픽셀쯤이라 확대가 바뀌면 다시 만든다.
///  · 고른 부재: 그림 위에 겹친 둘째 그림(같은 카메라)에 흰 막과 굵은 분홍 테두리로 덧그린다 — 다른 부재에 가려져 있어도 보이고,
///    부재의 제 색(위치 색)은 막 아래로 비친다.
///  · 찍기: Core 의 Picker 가 시선으로 직접 센다(WPF 의 찍기 기능을 쓰지 않는다 — 화면 없이도 같은 답).
///  · 마우스: 왼쪽 = 찍기 / 끌면 네모로 고르기(왼→오 온전히 든 것 · 오→왼 걸친 것까지) · 굴림 = 확대(마우스 자리) ·
///           가운데 끌기 = 옮기기 · Shift + 가운데 끌기 또는 오른쪽 끌기 = 돌리기 · 가운데 두 번 누르기 = 화면 맞춤. (Revit 과 같은 손짓)
///  · 층을 나눠 그리기(layered — 겹침 검사 창 · 수량산출 창. 켜지 않으면 위의 그리기 그대로다): 부재마다 그리는 방식(MeshStyle)을 받아
///    그림을 넷으로 겹친다 — 바닥(면 + 모서리 · 둘레 부재의 모서리만) → 옅은 면(층을 통째로 옅게 섞는다: 층 안에서는 불투명하게 그려
///    깊이가 맞고, 반투명 재료를 쓰지 않으니 그리는 차례 문제가 없다) → 맨 위(제 깊이 판정을 따로 가져 아래 층에 가려지지 않는다) → 고른 부재.
///    모두 Solid 면 층을 나누지 않은 그리기와 같은 그림이다(고른 부재의 덧그림까지 — R48: 수량산출 창의 ① 이 그대로 쓴다).
/// </summary>
public sealed class Model3DView : Grid
{
    private const double DragPixels = 4;          // 이만큼 넘게 움직여야 끌기다(손떨림은 찍기로 친다)
    private const double EdgePixels = 1.25;       // 모서리 띠의 폭(화면 픽셀)
    private const double PickEdgePixels = 3.0;    // 고른 부재의 테두리 폭(화면 픽셀)
    private const double EdgeLift = 0.003;        // 모서리 띠를 면에서 띄우는 높이(m)
    private const double TintOpacity = 0.34;      // 옅은 면의 층을 섞는 세기(층을 나눠 그릴 때)

    private List<MeshData> _meshes;               // 넘겨받은 겉면을 옮겨 둔 것(Replace 가 한 칸씩 · SetMeshes 가 통째로 바꾼다)
    private readonly bool _dark;
    private Box3 _scene;                          // 모든 겉면을 싼 상자(카메라의 앞뒤 면) — Replace 가 다시 센다
    private readonly OrthographicCamera _eye = new();
    private readonly Viewport3D _main = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Viewport3D _over = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Model3DGroup _bodies = new(), _picked = new();
    private readonly DirectionalLight _headlight = new(Color.FromRgb(112, 112, 112), new Vector3D(0, 0, -1));
    private readonly bool _layered;                                   // 층을 나눠 그린다(겹침 검사 창)
    private readonly Viewport3D? _tint, _top;                         // 옅은 면의 층 · 맨 위 층(층을 나눠 그릴 때만 있다)
    private readonly Model3DGroup _tintBodies = new(), _topModels = new();
    private readonly List<DirectionalLight> _layerHeadlights = new();
    private readonly Dictionary<int, Material> _flat = new();         // 빛에 상관없이 그 색 그대로인 재료(색칠한 모서리)
    private MeshGeometry3D?[] _body;
    private GeometryModel3D?[] _bodyModel, _edgeModel;
    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<int, MeshGeometry3D?> _pickOutline = new();   // 고른 부재의 굵은 테두리(확대가 바뀌면 다시 만든다)
    private readonly Rectangle _band = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false, StrokeThickness = 1.2 };
    private readonly Canvas _layer = new() { IsHitTestVisible = false };
    private readonly Canvas _labels = new();                                            // 3D 자리에 띄운 글 딱지(③ 길이 숫자 — R60 · 딱지만 눌린다 — R61c)
    private readonly List<(Vec3 at, Border view, Vec3 from, Vec3 to)> _labelViews = new();
    private int _pickedLabel = -1;                                                      // 마우스를 올린 숫자(R61c — 그 상자 · 그 선을 주황으로)
    private System.Windows.Shapes.Path? _pickedLine;
    private string? _markedText;                                                        // 식 띠에서 마우스를 올린 숫자(R63 — 같은 글의 딱지 전부 · 그 선들을 주황으로)
    private System.Windows.Shapes.Path? _markedLines;
    private Brush? _labelBack, _labelLine, _labelText, _pickBack, _pickLine;
    // 3D 자리의 점선(③ 콘크리트 숫자 — R61b): 더하는 도형 · 빼는 도형(빨강) · 잰 선. 갈래마다 길 하나(소프트웨어로만 그리는 Revit 에서 가볍게 — 계획 검토 L15).
    private readonly Canvas _guideLayer = new() { IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path[] _guidePaths = new System.Windows.Shapes.Path[3];
    private List<(IReadOnlyList<Vec3> points, bool closed, int kind)> _guides = new();
    private readonly Material _edgeMaterial, _pickFill, _pickEdge;
    private double _edgePixelSize = -1;           // 모서리 띠를 만든 때의 화면 1픽셀 길이(m)
    private bool _fitted;
    private int _hover = -1;

    private enum Drag { None, Pending, Band, Pan, Orbit }
    private Drag _drag = Drag.None;
    private Point _down, _last;
    private bool _downCtrl;
    private MouseButton _dragButton;              // 끌기를 시작한 단추 — 그 단추를 뗄 때만 끝난다
    private Vec3? _pivot;

    public OrbitCamera Camera { get; } = new();

    /// <summary>끌고 있는 중인가(돌리기 · 옮기기 · 네모).</summary>
    public bool Dragging => _drag is Drag.Band or Drag.Pan or Drag.Orbit;

    /// <summary>단추가 눌려 있는가(끌기 시작 전의 누름 포함) — 이때의 Esc 는 누른 것을 그만두는 데 쓴다.</summary>
    public bool Pressed => _drag != Drag.None;

    /// <summary>마지막으로 찍은 자리(Clicked 를 알릴 때의 화면 좌표).</summary>
    public Point ClickPoint { get; private set; }

    /// <summary>그 부재를 그릴 것인가(차례 = 넘겨준 겉면의 차례).</summary>
    public Func<int, bool> Shown { get; set; } = _ => true;

    /// <summary>그 부재의 색.</summary>
    public Func<int, (byte r, byte g, byte b)> ColorOf { get; set; } = _ => (ZonePalette.Todo.r, ZonePalette.Todo.g, ZonePalette.Todo.b);

    /// <summary>그 부재가 골라져 있는가.</summary>
    public Func<int, bool> Picked { get; set; } = _ => false;

    /// <summary>그 부재를 그리는 방식(층을 나눠 그릴 때만 본다).</summary>
    public Func<int, MeshStyle> StyleOf { get; set; } = _ => MeshStyle.Solid;

    /// <summary>
    /// 그 부재를 찍을 수 있는가(보이는 것 가운데) — 찍히지 않는 것은 시선이 지나간다. 수량산출 창 ② 는 줄을 고른 동안 옅게 그린 부재를 끈다
    /// (그 뒤의 옅은 판을 찍을 수 있게 — R48). 돌리기의 축은 이것과 상관없이 마우스 밑의 것으로 잡는다.
    /// </summary>
    public Func<int, bool> Pickable { get; set; } = _ => true;

    /// <summary>
    /// 모서리만 그린 것(MeshStyle.Ghost)도 찍히게 한다 — 맨 위 층 · 면이 있는 것 다음에(Pickable 이 허락한 것만). ② 거푸집: 줄을 고르면 나머지 판은
    /// 윤곽선만이지만 눌러 바로 옮겨 고를 수 있게(JACK 2026-10-21). 처음 값 = 찍히지 않음(겹침 창의 둘레 부재).
    /// </summary>
    public bool GhostPickable { get; set; }

    /// <summary>둘레 부재(MeshStyle.Ghost)의 모서리 색.</summary>
    public (byte r, byte g, byte b) GhostEdge { get; set; }

    /// <summary>왼쪽 단추로 끌어 네모로 고르기를 받는가(받지 않으면 끌어도 누른 자리를 찍은 것으로 친다).</summary>
    public bool BandSelect { get; set; } = true;

    /// <summary>부재를 찍었다(차례, Ctrl). 빈 곳이면 −1.</summary>
    public event Action<int, bool>? Clicked;

    /// <summary>끌어서 고른 부재들(차례들, Ctrl).</summary>
    public event Action<IReadOnlyList<int>, bool>? Banded;

    /// <summary>마우스 밑의 부재가 바뀌었다(없으면 −1).</summary>
    public event Action<int>? Hovered;

    /// <summary>마우스 처리에서 오류가 났다(끌던 것은 그만두었다) — 창에 알린다.</summary>
    public event Action<Exception>? Failed;

    /// <param name="layered">층을 나눠 그린다(StyleOf 를 본다). 주지 않으면 「위치·부위 정하기」의 그리기 그대로다.</param>
    public Model3DView(IReadOnlyList<MeshData> meshes, bool dark, bool layered = false)
    {
        _meshes = meshes.ToList();
        _dark = dark;
        _layered = layered;
        GhostEdge = dark ? ((byte)0x5C, (byte)0x64, (byte)0x70) : ((byte)0xB3, (byte)0xBA, (byte)0xC6);
        Focusable = true;
        FocusVisualStyle = null;                      // 글쇠를 누르면 그림 둘레에 점선 네모가 생긴다 — 그리지 않는다
        ClipToBounds = true;
        // 빈 곳도 마우스를 받게 바탕을 칠한다 — 위는 밝고 아래로 갈수록 조금 짙게(그림이 떠 보인다).
        var back = new LinearGradientBrush(dark ? Color.FromRgb(0x2A, 0x2E, 0x35) : Color.FromRgb(0xFF, 0xFF, 0xFF),
                                           dark ? Color.FromRgb(0x1A, 0x1C, 0x21) : Color.FromRgb(0xE8, 0xEC, 0xF2), 90);
        back.Freeze();
        Background = back;

        var scene = Box3.Empty;
        foreach (var mesh in meshes) scene = scene.With(mesh.Box);
        _scene = scene;

        _edgeMaterial = Solid(dark ? Color.FromRgb(0x0E, 0x10, 0x14) : Color.FromRgb(0x2A, 0x2E, 0x36));
        _pickFill = Solid(Color.FromArgb(96, 255, 255, 255));
        _pickEdge = Solid(Color.FromRgb(ZonePalette.Picked.r, ZonePalette.Picked.g, ZonePalette.Picked.b));

        _body = new MeshGeometry3D?[meshes.Count];
        _bodyModel = new GeometryModel3D?[meshes.Count];
        _edgeModel = new GeometryModel3D?[meshes.Count];
        for (int i = 0; i < meshes.Count; i++) Make(i);

        // 빛: 어느 쪽에서 봐도 면이 읽히게 — 둘레 빛 + 카메라가 보는 방향의 빛 + 위에서 비스듬히 오는 빛.
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(104, 104, 104)));
        lights.Children.Add(_headlight);
        lights.Children.Add(new DirectionalLight(Color.FromRgb(76, 76, 76), new Vector3D(-0.35, 0.25, -1)));
        var world = new Model3DGroup();
        world.Children.Add(lights);
        world.Children.Add(_bodies);
        _main.Camera = _eye;
        _main.Children.Add(new ModelVisual3D { Content = world });

        // 고른 부재의 덧그림: 빛에 상관없이 같은 색(둘레 빛만).
        var over = new Model3DGroup();
        over.Children.Add(new AmbientLight(Colors.White));
        over.Children.Add(_picked);
        _over.Camera = _eye;
        _over.Children.Add(new ModelVisual3D { Content = over });

        _layer.Children.Add(_band);
        Children.Add(_main);
        if (layered)
        {
            _tint = Layer(_tintBodies, TintOpacity);
            _top = Layer(_topModels, 1);
            Children.Add(_tint);
            Children.Add(_top);
        }
        Children.Add(_over);
        Children.Add(_guideLayer);
        Children.Add(_labels);
        Children.Add(_layer);
        Refresh();
    }

    /// <summary>3D 자리(장면 좌표)에 글 딱지를 띄운다 — 보는 자리를 바꾸면 따라 움직인다. 빈 목록 = 지움(③ 길이 숫자 — R60).</summary>
    public void SetLabels(IReadOnlyList<(Vec3 at, string text)> labels) => SetLabels(labels.Select(l => (l.at, l.text, l.at, l.at)).ToList());

    /// <summary>
    /// 글 딱지 + 그 숫자가 재는 선분(from → to · 같으면 선 없음). 딱지에 마우스를 올리면 그 상자와 그 선이 주황 · 내리면 풂
    /// (R61c — JACK 2026-10-26 «치수 누르면 어느 치수인지 해당 선이 색이 바뀌게 · 누른 치수 네모도» → «누르는 게 아니고 마우스를 갖다 대면»).
    /// 누르기는 딱지가 받지 않는다 — 그 밑의 부재 · 판 찍기로 간다.
    /// </summary>
    public void SetLabels(IReadOnlyList<(Vec3 at, string text, Vec3 from, Vec3 to)> labels)
    {
        _labels.Children.Clear();
        _labelViews.Clear();
        if (_pickedLabel >= 0) LabelHovered?.Invoke(null);   // 올려 둔 숫자가 사라진다 — 식의 칠도 푼다
        _pickedLabel = -1;
        _markedText = null;
        if (_labelBack == null)
        {
            Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
            _labelBack = Frozen(_dark ? Color.FromArgb(0xE6, 0x26, 0x2A, 0x31) : Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
            _labelLine = Frozen(_dark ? Color.FromRgb(0x5C, 0x64, 0x70) : Color.FromRgb(0x8A, 0x94, 0xA3));
            _labelText = Frozen(_dark ? Color.FromRgb(0xE8, 0xEA, 0xED) : Color.FromRgb(0x1F, 0x23, 0x28));
            _pickBack = Frozen(Color.FromRgb(0xF2, 0x8C, 0x28));     // 주황 — 더함 파랑 · 뺌 빨강 점선과 헷갈리지 않게
            _pickLine = Frozen(Color.FromRgb(0xB8, 0x5C, 0x0A));
        }
        foreach (var (at, words, from, to) in labels)
        {
            int index = _labelViews.Count;
            var view = new Border
            {
                Background = _labelBack, BorderBrush = _labelLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1),
                Child = new TextBlock { Text = words, FontSize = 11, Foreground = _labelText },
            };
            // 마우스를 올린 동안만 고른다(끄는 중에는 바꾸지 않는다 — 돌리다 지나가는 숫자마다 깜빡이지 않게).
            view.MouseEnter += (_, _) => { if (_drag == Drag.None) PickLabel(index); };
            view.MouseLeave += (_, _) => { if (_pickedLabel == index) PickLabel(-1); };
            _labels.Children.Add(view);
            _labelViews.Add((at, view, from, to));
        }
        PlaceLabels();
    }

    /// <summary>마우스를 올린 숫자의 글(내리면 null) — ③ 아래 식에서 그 숫자를 칠한다(R61c).</summary>
    public event Action<string?>? LabelHovered;

    // 숫자 하나를 고른다(−1 = 풂): 그 상자는 주황 바탕 · 흰 글, 그 숫자가 재는 선은 주황 실선.
    private void PickLabel(int index)
    {
        bool changed = _pickedLabel != index;
        _pickedLabel = index;
        Restyle();
        PlaceLabels();
        if (changed) LabelHovered?.Invoke(index >= 0 && _labelViews[index].view.Child is TextBlock tb ? tb.Text : null);
    }

    /// <summary>
    /// ③ 아래 식 띠의 숫자에 마우스를 올렸다(내리면 null): 같은 숫자의 딱지 전부와 그 숫자들이 재는 선을 주황으로
    /// (R63 — JACK 2026-10-26 «수식창에서 해당 숫자에 마우스를 올렸을 때 해당 치수들이 뷰에서도 바뀌게» · R61c 의 거꾸로). 각도 딱지는 «°» 를 떼고 견준다.
    /// </summary>
    public void MarkLabels(string? text)
    {
        if (text == _markedText) return;
        _markedText = text;
        Restyle();
        PlaceLabels();
    }

    private bool MarkedByText(int i) => _markedText != null && _labelViews[i].view.Child is TextBlock t && t.Text.TrimEnd('°') == _markedText;

    // 딱지 색: 마우스를 올린 것(R61c) · 식 띠에서 가리킨 숫자(R63)는 주황 바탕 · 흰 글.
    private void Restyle()
    {
        for (int i = 0; i < _labelViews.Count; i++)
        {
            var (_, view, _, _) = _labelViews[i];
            bool picked = i == _pickedLabel || MarkedByText(i);
            view.Background = picked ? _pickBack : _labelBack;
            view.BorderBrush = picked ? _pickLine : _labelLine;
            if (view.Child is TextBlock t)
            {
                t.Foreground = picked ? Brushes.White : _labelText;
                t.FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal;
            }
            Panel.SetZIndex(view, picked ? 1 : 0);
        }
    }

    /// <summary>시험용: 식 띠에서 가리킨 숫자로 칠한 딱지 · 그 선의 수.</summary>
    public IReadOnlyList<int> MarkedLabels => Enumerable.Range(0, _labelViews.Count).Where(MarkedByText).ToList();
    public int MarkedLineCount => _markedLines?.Data is GeometryGroup g ? g.Children.Count : 0;
    public IReadOnlyList<double> LabelSegmentLengths => _labelViews.Select(l => (l.to - l.from).Length).ToList();

    /// <summary>시험용: 마우스를 올린 숫자(−1 = 없음) · 그 딱지 · 강조한 선이 화면에 있는가.</summary>
    public int PickedLabel => _pickedLabel;
    public Border? LabelView(int index) => index >= 0 && index < _labelViews.Count ? _labelViews[index].view : null;
    public bool PickedLineShown => _pickedLine?.Data != null;

    /// <summary>
    /// 3D 자리(장면 좌표)의 점선들(③ 콘크리트 숫자 — R61b): kind 0 = 더하는 도형(파랑) · 1 = 빼는 도형(빨강 — JACK «공제는 빨간 점선») · 2 = 잰 선(회색).
    /// 딱지처럼 보는 자리가 바뀌면 다시 놓는다 · 빈 목록 = 지움.
    /// </summary>
    public void SetGuides(IReadOnlyList<(IReadOnlyList<Vec3> points, bool closed, int kind)> guides)
    {
        _guides = guides.Where(g => g.points.Count >= 2 && g.kind is >= 0 and <= 2).ToList();
        if (_guidePaths[0] == null)
        {
            var colors = _dark
                ? new[] { Color.FromRgb(0x6F, 0xA8, 0xFF), Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xA0, 0xA8, 0xB4) }
                : new[] { Color.FromRgb(0x1F, 0x5F, 0xD0), Color.FromRgb(0xD0, 0x30, 0x30), Color.FromRgb(0x6E, 0x78, 0x86) };
            for (int k = 0; k < 3; k++)
            {
                var brush = new SolidColorBrush(colors[k]);
                brush.Freeze();
                _guidePaths[k] = new System.Windows.Shapes.Path
                {
                    Stroke = brush, StrokeThickness = k == 2 ? 1.1 : 1.6, StrokeDashArray = k == 2 ? new DoubleCollection { 2, 3 } : new DoubleCollection { 5, 3 },
                    IsHitTestVisible = false,
                };
                _guideLayer.Children.Add(_guidePaths[k]);
            }
        }
        PlaceGuides();
    }

    /// <summary>시험용: 점선 수(갈래마다).</summary>
    public int GuideCount(int kind) => _guides.Count(g => g.kind == kind);

    // 점선을 지금 카메라로 화면에 놓는다(갈래마다 StreamGeometry 하나).
    private void PlaceGuides()
    {
        if (_guidePaths[0] == null) return;
        for (int k = 0; k < 3; k++)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
                foreach (var (points, closed, kind) in _guides)
                {
                    if (kind != k) continue;
                    ctx.BeginFigure(ScreenOf(points[0]), false, closed);
                    ctx.PolyLineTo(points.Skip(1).Select(ScreenOf).ToList(), true, false);
                }
            geometry.Freeze();
            _guidePaths[k].Data = geometry;
        }
    }

    // 누른 숫자가 재는 선(주황 실선 — 점선 위). 선분이 없는 딱지(가운데 식 · 각도)는 상자만 바뀐다.
    private void PlacePickedLine()
    {
        if (_pickedLine == null)
        {
            _pickedLine = new System.Windows.Shapes.Path { Stroke = _pickBack ?? Brushes.Orange, StrokeThickness = 3.2, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _guideLayer.Children.Add(_pickedLine);
        }
        PlaceMarkedLines();
        if (_pickedLabel < 0 || _pickedLabel >= _labelViews.Count || (_labelViews[_pickedLabel].to - _labelViews[_pickedLabel].from).Length < 1e-9)
        {
            _pickedLine.Data = null;
            return;
        }
        var (_, _, from, to) = _labelViews[_pickedLabel];
        var line = new LineGeometry(ScreenOf(from), ScreenOf(to));
        line.Freeze();
        _pickedLine.Data = line;
    }

    // 식 띠에서 가리킨 숫자들이 재는 선(R63 — 마우스를 올린 숫자의 선과 같은 주황 실선).
    private void PlaceMarkedLines()
    {
        if (_markedLines == null)
        {
            if (_markedText == null) return;
            _markedLines = new System.Windows.Shapes.Path { Stroke = _pickBack ?? Brushes.Orange, StrokeThickness = 3.2, IsHitTestVisible = false, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _guideLayer.Children.Add(_markedLines);
        }
        var group = new GeometryGroup();
        for (int i = 0; i < _labelViews.Count; i++)
        {
            var (_, _, from, to) = _labelViews[i];
            if (!MarkedByText(i) || (to - from).Length < 1e-9) continue;
            group.Children.Add(new LineGeometry(ScreenOf(from), ScreenOf(to)));
        }
        group.Freeze();
        _markedLines.Data = group.Children.Count > 0 ? group : null;
    }

    /// <summary>시험용: 띄운 딱지의 글과 화면 자리(가운데).</summary>
    public IReadOnlyList<(string text, Point at)> Labels =>
        _labelViews.Select(l => (((TextBlock)l.view.Child).Text, new Point(Canvas.GetLeft(l.view) + l.view.DesiredSize.Width / 2, Canvas.GetTop(l.view) + l.view.DesiredSize.Height / 2))).ToList();

    /// <summary>시험용: 강조한 선의 화면 끝점(없으면 null).</summary>
    public (Point a, Point b)? PickedLine => _pickedLine?.Data is LineGeometry g ? (g.StartPoint, g.EndPoint) : null;

    // 딱지 · 점선을 지금 카메라로 화면에 놓는다(딱지는 가운데가 그 자리).
    private void PlaceLabels()
    {
        PlaceGuides();
        PlacePickedLine();
        foreach (var (at, view, _, _) in _labelViews)
        {
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var p = ScreenOf(at);
            Canvas.SetLeft(view, p.X - view.DesiredSize.Width / 2);
            Canvas.SetTop(view, p.Y - view.DesiredSize.Height / 2);
        }
    }

    // 그 차례의 몸과 모서리를 _meshes[i] 로 만든다. 모서리 띠는 화면 1픽셀의 길이를 안 뒤에만(SyncCamera 가 처음 만든다).
    private void Make(int i)
    {
        _body[i] = null;
        _bodyModel[i] = _edgeModel[i] = null;
        var mesh = _meshes[i];
        if (mesh.T.Length == 0) return;
        _body[i] = Geometry(mesh.P, mesh.T);
        _bodyModel[i] = new GeometryModel3D { Geometry = _body[i] };
        _edgeModel[i] = new GeometryModel3D { Material = _edgeMaterial, BackMaterial = _edgeMaterial };
        if (_edgePixelSize <= 0) return;
        var (p, t) = EdgeStrips.Build(mesh, _edgePixelSize * EdgePixels, EdgeLift);
        _edgeModel[i]!.Geometry = t.Length > 0 ? Geometry(p, t) : null;
    }

    /// <summary>
    /// 그 차례의 겉면을 바꿔 그린다(겹침 창에서 결합한 뒤 — 부재의 모양이 바뀌었다). 보는 자리 · 색 · 장면의 크기는 그대로다.
    /// 마우스 밑에 있던 것이면 다음 움직임에서 다시 찾는다.
    /// </summary>
    public void Replace(int index, MeshData mesh)
    {
        if (index < 0 || index >= _meshes.Count) return;
        var before = (_meshes[index], _body[index], _bodyModel[index], _edgeModel[index]);
        try
        {
            _meshes[index] = mesh;
            Make(index);
        }
        catch
        {
            // 새 겉면을 만들지 못했다: 옛 그림을 그대로 두고(그림과 찍기가 어긋나지 않게) 부르는 쪽에 알린다.
            (_meshes[index], _body[index], _bodyModel[index], _edgeModel[index]) = before;
            throw;
        }
        _pickOutline.Remove(index);
        // 장면 상자를 다시 센다(상자로 대신 그렸던 부재가 이번에 제대로 읽혀 커질 수 있다 — 카메라의 앞뒤 면이 그것을 자르지 않게).
        var scene = Box3.Empty;
        foreach (var each in _meshes) scene = scene.With(each.Box);
        _scene = scene;
        SyncCamera();       // 앞뒤 면을 새 상자로(확대가 그대로면 모서리 띠는 다시 만들지 않는다)
        Refresh();
    }

    /// <summary>
    /// 그릴 겉면을 통째로 바꾼다(수량산출 창의 걸음을 넘길 때 — ① 부재 → ② 부재 + 거푸집 조각, JACK 2026-10-15 «3D 뷰 위치부분은 고정»).
    /// 보는 자리(카메라)는 그대로다. Shown · ColorOf · Picked · StyleOf 는 새 차례로 불리므로 부르는 쪽이 먼저(또는 바로 뒤에) 갈아 끼운다.
    /// 끌던 것 · 마우스 밑의 것 · 고른 부재의 테두리는 지운다.
    /// </summary>
    public void SetMeshes(IReadOnlyList<MeshData> meshes)
    {
        CancelDrag();
        // 전에도 그리던 겉면(같은 MeshData)은 만든 면 · 모서리 띠를 다시 쓴다 — ② 에서 대응표를 고치거나 판을 빼면 겉면을 통째로 갈아 끼우는데
        // 바뀐 판은 몇 장뿐이다(R48). 모서리 띠는 지금 확대로 만든 것이다(확대가 바뀌면 SyncCamera 가 모두 다시 만든다).
        var made = new Dictionary<MeshData, (MeshGeometry3D body, Geometry3D? edge)>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _meshes.Count; i++)
            if (_body[i] is { } was) made.TryAdd(_meshes[i], (was, _edgeModel[i]?.Geometry));
        _meshes = meshes.ToList();
        _body = new MeshGeometry3D?[_meshes.Count];
        _bodyModel = new GeometryModel3D?[_meshes.Count];
        _edgeModel = new GeometryModel3D?[_meshes.Count];
        _pickOutline.Clear();
        for (int i = 0; i < _meshes.Count; i++)
        {
            if (made.TryGetValue(_meshes[i], out var old))
            {
                _body[i] = old.body;
                _bodyModel[i] = new GeometryModel3D { Geometry = old.body };
                _edgeModel[i] = new GeometryModel3D { Material = _edgeMaterial, BackMaterial = _edgeMaterial, Geometry = old.edge };
            }
            else Make(i);                                     // 모서리 띠는 지금 확대로 바로 만든다(처음 맞춘 뒤면)
        }
        var scene = Box3.Empty;
        foreach (var each in _meshes) scene = scene.With(each.Box);
        _scene = scene;
        bool hovered = _hover >= 0;
        _hover = -1;
        if (_fitted) SyncCamera();                            // 앞뒤 면만 새 상자로(보는 자리 · 확대는 그대로)
        Refresh();
        // 마우스가 그림 위에 있으면 새 겉면에서 다시 찾는다 — 줄을 골라 겉면을 갈아 끼워도 마우스 밑 글이 사라지지 않게(JACK 2026-10-23 줄 몫 덧그림).
        int again = hovered && IsMouseOver && _drag == Drag.None ? IndexAt(Mouse.GetPosition(this)) : -1;
        if (again >= 0) SetHover(again);
        else if (hovered) Hovered?.Invoke(-1);
    }

    /// <summary>지금 그리는 겉면의 수.</summary>
    public int MeshCount => _meshes.Count;

    /// <summary>그 차례의 지금 겉면(Replace 로 바꾼 것까지).</summary>
    public MeshData MeshAt(int index) => _meshes[index];

    /// <summary>그 차례를 그리는 데 쓰고 있는 세모의 수(시험에서 본다 — Replace 가 그림까지 바꿨는가).</summary>
    public int DrawnTriangles(int index) => index >= 0 && index < _body.Length && _body[index] is { } body ? body.TriangleIndices.Count / 3 : 0;

    /// <summary>그 차례를 그리는 데 쓰고 있는 면의 범위(시험에서 본다 — 그리는 모양이 받은 겉면과 같은 자리인가). 그리지 않으면 빈 상자.</summary>
    public Box3 DrawnBox(int index)
    {
        if (index < 0 || index >= _body.Length || _body[index] is not { } body || body.Bounds.IsEmpty) return Box3.Empty;
        var b = body.Bounds;
        return new Box3(new Vec3(b.X, b.Y, b.Z), new Vec3(b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ));
    }

    // 층 하나: 같은 카메라 · 같은 빛으로, 제 깊이 판정을 따로 가진 그림(아래 층에 가려지지 않는다). opacity < 1 이면 층을 통째로 옅게 섞는다.
    private Viewport3D Layer(Model3DGroup models, double opacity)
    {
        var head = new DirectionalLight(Color.FromRgb(112, 112, 112), new Vector3D(0, 0, -1));
        _layerHeadlights.Add(head);
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(104, 104, 104)));
        lights.Children.Add(head);
        lights.Children.Add(new DirectionalLight(Color.FromRgb(76, 76, 76), new Vector3D(-0.35, 0.25, -1)));
        var world = new Model3DGroup();
        world.Children.Add(lights);
        world.Children.Add(models);
        var view = new Viewport3D { IsHitTestVisible = false, ClipToBounds = true, Camera = _eye, Opacity = opacity };
        view.Children.Add(new ModelVisual3D { Content = world });
        return view;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Material Solid(Color color)
    {
        var material = new DiffuseMaterial(Brush(color));
        material.Freeze();
        return material;
    }

    private static MeshGeometry3D Geometry(double[] p, int[] t)
    {
        var points = new Point3DCollection(p.Length / 3);
        for (int i = 0; i + 2 < p.Length; i += 3) points.Add(new Point3D(p[i], p[i + 1], p[i + 2]));
        var mesh = new MeshGeometry3D { Positions = points, TriangleIndices = new Int32Collection(t) };
        mesh.Freeze();
        return mesh;
    }

    private Material MaterialOf((byte r, byte g, byte b) c)
    {
        int key = (c.r << 16) | (c.g << 8) | c.b;
        if (!_materials.TryGetValue(key, out var material)) _materials[key] = material = Solid(Color.FromRgb(c.r, c.g, c.b));
        return material;
    }

    // 빛에 상관없이 그 색 그대로인 재료(색칠한 모서리): 검은 바탕에 스스로 빛나는 색을 얹는다 — 면의 방향에 따라 선의 색이 달라지지 않는다.
    private Material FlatOf((byte r, byte g, byte b) c)
    {
        int key = (c.r << 16) | (c.g << 8) | c.b;
        if (_flat.TryGetValue(key, out var material)) return material;
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brushes.Black));
        group.Children.Add(new EmissiveMaterial(Brush(Color.FromRgb(c.r, c.g, c.b))));
        group.Freeze();
        return _flat[key] = group;
    }

    // ── 그리기 ───────────────────────────────────────────────────────────────────

    /// <summary>보이는 것 · 색 · 고른 것이 바뀌었다: 그림을 다시 맞춘다.</summary>
    public void Refresh()
    {
        _bodies.Children.Clear();
        _picked.Children.Clear();
        if (_layered) { RefreshLayers(); return; }
        for (int i = 0; i < _meshes.Count; i++)
        {
            var body = _bodyModel[i];
            if (body == null || !Shown(i)) continue;
            var color = ColorOf(i);
            if (i == _hover) color = Lighter(color);
            body.Material = body.BackMaterial = MaterialOf(color);
            _bodies.Children.Add(body);
            if (_edgeModel[i] is { Geometry: not null } edge) _bodies.Children.Add(edge);
            if (Picked(i)) AddPicked(i);
        }
    }

    // 고른 부재의 덧그림: 흰 막 + 굵은 분홍 테두리(테두리는 확대가 바뀔 때까지 담아 둔다).
    private void AddPicked(int i)
    {
        _picked.Children.Add(new GeometryModel3D { Geometry = _body[i], Material = _pickFill, BackMaterial = _pickFill });
        if (_edgePixelSize <= 0) return;
        if (!_pickOutline.TryGetValue(i, out var outline))
        {
            var (p, t) = EdgeStrips.Build(_meshes[i], _edgePixelSize * PickEdgePixels, EdgeLift * 2);
            _pickOutline[i] = outline = t.Length > 0 ? Geometry(p, t) : null;
        }
        if (outline != null) _picked.Children.Add(new GeometryModel3D { Geometry = outline, Material = _pickEdge, BackMaterial = _pickEdge });
    }

    // 층을 나눠 그릴 때: 부재마다 그리는 방식에 따라 제 층에 넣고, 고른 것은 맨 위에 덧그린다(겹침 창은 고르는 것이 없어 Picked 가 늘 거짓 —
    // 수량산출 창 ① 의 고른 부재 · ② 의 찍은 판).
    private void RefreshLayers()
    {
        _tintBodies.Children.Clear();
        _topModels.Children.Clear();
        for (int i = 0; i < _meshes.Count; i++)
        {
            var body = _bodyModel[i];
            if (body == null || !Shown(i)) continue;
            var color = ColorOf(i);
            if (i == _hover) color = Lighter(color);
            var edge = _edgeModel[i] is { Geometry: not null } strip ? strip : null;
            var style = StyleOf(i);
            if (style != MeshStyle.Ghost && Picked(i)) AddPicked(i);
            switch (style)
            {
                case MeshStyle.Ghost:
                    if (edge != null) { edge.Material = edge.BackMaterial = FlatOf(GhostEdge); _bodies.Children.Add(edge); }
                    break;
                case MeshStyle.Tint:
                    body.Material = body.BackMaterial = MaterialOf(color);
                    _tintBodies.Children.Add(body);
                    if (edge != null) { edge.Material = edge.BackMaterial = FlatOf(Darker(color)); _topModels.Children.Add(edge); }
                    break;
                case MeshStyle.Top:
                    body.Material = body.BackMaterial = MaterialOf(color);
                    _topModels.Children.Add(body);
                    if (edge != null) { edge.Material = edge.BackMaterial = FlatOf(Darker(color)); _topModels.Children.Add(edge); }
                    break;
                default:
                    body.Material = body.BackMaterial = MaterialOf(color);
                    _bodies.Children.Add(body);
                    if (edge != null) { edge.Material = edge.BackMaterial = _edgeMaterial; _bodies.Children.Add(edge); }
                    break;
            }
        }
    }

    private static (byte r, byte g, byte b) Lighter((byte r, byte g, byte b) c) =>
        ((byte)(c.r + (255 - c.r) * 0.38), (byte)(c.g + (255 - c.g) * 0.38), (byte)(c.b + (255 - c.b) * 0.38));

    private static (byte r, byte g, byte b) Darker((byte r, byte g, byte b) c) => ((byte)(c.r * 0.62), (byte)(c.g * 0.62), (byte)(c.b * 0.62));

    // 카메라가 바뀌었다: WPF 카메라와 빛을 맞추고, 확대가 달라졌으면 모서리 띠를 다시 만든다.
    private void SyncCamera()
    {
        var (position, near, far) = Camera.Placement(_scene);
        var forward = Camera.Forward;
        var up = Camera.Up;
        _eye.Position = new Point3D(position.X, position.Y, position.Z);
        _eye.LookDirection = new Vector3D(forward.X, forward.Y, forward.Z);
        _eye.UpDirection = new Vector3D(up.X, up.Y, up.Z);
        _eye.Width = Camera.Width;
        _eye.NearPlaneDistance = near;
        _eye.FarPlaneDistance = far;
        _headlight.Direction = new Vector3D(forward.X, forward.Y, forward.Z);
        foreach (var head in _layerHeadlights) head.Direction = _headlight.Direction;
        PlaceLabels();   // 돌리기 · 옮기기에도 따라간다(아래 확대 되돌아가기 앞 — 계획 검토 M3)

        double pixel = Camera.PixelSize;
        if (_edgePixelSize > 0 && Math.Abs(pixel - _edgePixelSize) <= _edgePixelSize * 0.12) return;
        _edgePixelSize = pixel;
        _pickOutline.Clear();
        for (int i = 0; i < _meshes.Count; i++)
        {
            if (_edgeModel[i] is not { } edge) continue;
            var (p, t) = EdgeStrips.Build(_meshes[i], pixel * EdgePixels, EdgeLift);
            edge.Geometry = t.Length > 0 ? Geometry(p, t) : null;
        }
        Refresh();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (ActualWidth < 20 || ActualHeight < 20) return;
        // 창 크기가 바뀌어도 같은 크기로 보이게: 화면 1픽셀의 길이를 지킨다.
        double pixel = Camera.PixelSize;
        Camera.ViewW = ActualWidth;
        Camera.ViewH = ActualHeight;
        if (!_fitted) { Camera.Fit(_scene); _fitted = true; }
        else Camera.Width = pixel * ActualWidth;
        SyncCamera();
    }

    /// <summary>모든 부재가 다 보이게 맞춘다(보는 방향은 그대로).</summary>
    public void Fit()
    {
        var box = Box3.Empty;
        for (int i = 0; i < _meshes.Count; i++)
            if (_bodyModel[i] != null && Shown(i)) box = box.With(_meshes[i].Box);
        Camera.Fit(box.IsEmpty ? _scene : box);
        SyncCamera();
    }

    /// <summary>미리 정한 방향으로 보고 화면에 맞춘다.</summary>
    public void Look(ViewPreset preset)
    {
        Camera.Look(preset);
        Fit();
    }

    /// <summary>그 부재가 화면 가운데에 넉넉히 보이게 한다(둘레도 보이게 여백을 크게 둔다).</summary>
    public void ZoomTo(int index)
    {
        if (index < 0 || index >= _meshes.Count || _meshes[index].Box.IsEmpty) return;
        Camera.Fit(_meshes[index].Box, margin: 1.2);
        SyncCamera();
    }

    /// <summary>그 상자가 화면 가운데에 넉넉히 보이게 한다. minWidth = 화면 가로가 이보다 좁아지지 않게(m) — 작은 것을 볼 때도 둘레가 보인다.</summary>
    public void ZoomTo(Box3 box, double margin, double minWidth)
    {
        if (box.IsEmpty) return;
        Camera.Fit(box, margin);
        if (Camera.Width < minWidth) Camera.Width = minWidth;
        SyncCamera();
    }

    /// <summary>모델의 점(장면 좌표)이 화면의 어디에 그려지는가(시험에서 본다).</summary>
    public Point ScreenOf(Vec3 p)
    {
        var s = Camera.Project(p);
        return new Point(s.x, s.y);
    }

    /// <summary>화면의 그 자리에서 맨 앞에 보이는 부재의 차례(없으면 −1).</summary>
    public int IndexAt(Point at)
    {
        var (origin, direction) = Camera.Ray(at.X, at.Y);
        if (_layered)
        {
            // 맨 위 층의 것은 다른 것에 가려져 있어도 보이므로 먼저 닿는다. 모서리만 그린 둘레 부재는 찍히지 않는다.
            int top = Picker.Pick(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) == MeshStyle.Top, origin, direction).index;
            if (top >= 0) return top;
            int body = Picker.Pick(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) is MeshStyle.Solid or MeshStyle.Tint, origin, direction).index;
            if (body >= 0 || !GhostPickable) return body;
            return Picker.Pick(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) == MeshStyle.Ghost, origin, direction).index;
        }
        return Picker.Pick(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i), origin, direction).index;
    }

    /// <summary>
    /// 화면의 그 자리에서 찍을 수 있는 것 전부 — 찍히는 차례대로(첫째 = IndexAt 의 답). 층을 나눠 그릴 때는 맨 위 층의 것이 먼저이고,
    /// 층 안에서는 가까운 것부터다. 겹쳐 그려진 것들을 다시 눌러 차례로 고를 때 쓴다.
    /// </summary>
    public IReadOnlyList<int> IndicesAt(Point at)
    {
        var (origin, direction) = Camera.Ray(at.X, at.Y);
        if (!_layered) return Picker.PickAll(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i), origin, direction).Select(h => h.index).ToList();
        var hits = Picker.PickAll(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) == MeshStyle.Top, origin, direction).Select(h => h.index).ToList();
        hits.AddRange(Picker.PickAll(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) is MeshStyle.Solid or MeshStyle.Tint, origin, direction).Select(h => h.index));
        if (GhostPickable)
            hits.AddRange(Picker.PickAll(_meshes, i => _bodyModel[i] != null && Shown(i) && Pickable(i) && StyleOf(i) == MeshStyle.Ghost, origin, direction).Select(h => h.index));
        return hits;
    }

    // ── 마우스 ───────────────────────────────────────────────────────────────────

    // 마우스 처리 하나를 감싼다: 오류가 나면 끌던 것을 그만두고 알린다(여기서 새는 오류는 창을 닫는다).
    private void Guard(Action work)
    {
        try { work(); }
        catch (Exception ex)
        {
            CancelDrag();
            Failed?.Invoke(ex);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Guard(() =>
        {
            Focus();
            if (e.ChangedButton == MouseButton.Middle && e.ClickCount == 2) { CancelDrag(); Fit(); e.Handled = true; return; }
            // 뗀 것을 듣지 못한 끌기가 남아 있으면 치운다: 그 단추를 다시 눌렀거나(그 사이에 뗀 적이 있다는 뜻) 그 단추가 지금 떼어져 있다.
            // 남겨 두면 이 누르기를 받지 않고(Press 가 거절), 뗄 때 옛 자리를 찍은 것으로 친다.
            if (_drag != Drag.None && (e.ChangedButton == _dragButton || !Held(e))) CancelDrag();
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!Press(e.GetPosition(this), e.ChangedButton, ctrl, shift)) return;
            // 마우스를 잡는다 — 그림 밖에서 떼어도 듣게. 잡지 못해도 그림 안에서 떼면 그대로 되고,
            // 밖에서 떼어 듣지 못한 끌기는 다음 움직임 · 누르기에서 치운다(위 · OnMouseMove).
            CaptureMouse();
            e.Handled = true;
        });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Guard(() =>
        {
            // 뗀 것을 듣지 못한 끌기(OnMouseDown 의 풀이)를 치운다 — 마우스를 잡지 못했을 때만 생기므로 그때만 본다.
            // 잡고 있는 보통 끌기에서는 보면 안 된다: 뗀 자리가 마지막 움직임과 다르면 WPF 가 떼기 앞에 움직임을 하나 끼워 보내는데,
            // 그때 단추는 이미 뗀 것으로 읽혀 멀쩡한 찍기 · 끌어 고르기를 지우게 된다(2026-10-13 검토 R24 가 재현).
            if (_drag != Drag.None && !IsMouseCaptured && !Held(e)) CancelDrag();
            MoveTo(e.GetPosition(this));
        });
    }

    // 끌기를 시작한 단추가 지금 눌려 있는가.
    private bool Held(MouseEventArgs e) => _dragButton switch
    {
        MouseButton.Left => e.LeftButton == MouseButtonState.Pressed,
        MouseButton.Right => e.RightButton == MouseButtonState.Pressed,
        MouseButton.Middle => e.MiddleButton == MouseButtonState.Pressed,
        _ => false,
    };

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == Drag.None || e.ChangedButton != _dragButton) return;
        Guard(() =>
        {
            // 차례가 중요하다. 마우스를 놓아 주면 WPF 가 그 자리에서 곧바로 OnLostMouseCapture 를 부르고, 거기서 CancelDrag 가 하던 것을 지운다.
            // 놓기를 먼저 하면 찍은 것 · 끌어 고른 것이 알려지기 전에 지워져, 찍어도 아무 일도 일어나지 않는다
            // (2026-10-13 JACK 이 Revit 에서 겪음 — 돌리기 · 옮기기는 끄는 동안 이미 되어 있어 멀쩡했다).
            var at = e.GetPosition(this);
            var ended = EndDrag();                          // ① 끝났다고 먼저 적는다
            if (IsMouseCaptured) ReleaseMouseCapture();     // ② 마우스를 놓는다(여기서 OnLostMouseCapture 가 불려도 지울 것이 없다)
            Finish(ended, at);                              // ③ 찍은 것 · 끌어 고른 것을 알린다
            e.Handled = true;
        });
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Guard(() =>
        {
            Wheel(e.GetPosition(this), e.Delta);
            e.Handled = true;
        });
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelDrag();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == Drag.None) SetHover(-1);
    }

    /// <summary>끌던 것을 그만둔다(Esc · 오류 · 마우스를 놓침).</summary>
    public void CancelDrag()
    {
        _drag = Drag.None;
        _band.Visibility = Visibility.Collapsed;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = _hover >= 0 ? Cursors.Hand : Cursors.Arrow;
    }

    // 아래 넷이 마우스 처리의 알맹이다(화면 좌표를 받는다). 마우스 이벤트가 이것을 부르고, 시험(tools/uipreview)도 같은 것을 부른다.

    /// <summary>단추를 눌렀다. 받았으면 true.</summary>
    public bool Press(Point at, MouseButton button, bool ctrl, bool shift)
    {
        if (!_fitted || _drag != Drag.None) return false;
        _down = _last = at;
        _downCtrl = ctrl;
        _dragButton = button;
        if (button == MouseButton.Left) _drag = Drag.Pending;
        else if (button == MouseButton.Right || (button == MouseButton.Middle && shift))
        {
            // 돌리기의 축: 마우스 밑에 부재가 있으면 그 자리, 없으면 바라보는 점.
            var (origin, direction) = Camera.Ray(at.X, at.Y);
            var hit = Picker.Pick(_meshes, i => _bodyModel[i] != null && Shown(i), origin, direction);
            _pivot = hit.index >= 0 ? origin + direction.Unit() * hit.distance : null;
            _drag = Drag.Orbit;
            SetHover(-1);
            Cursor = Cursors.SizeAll;
        }
        else if (button == MouseButton.Middle) { _drag = Drag.Pan; SetHover(-1); Cursor = Cursors.Hand; }
        else return false;
        return true;
    }

    /// <summary>마우스를 옮겼다(누른 채면 끌기, 아니면 마우스 밑의 부재를 알린다).</summary>
    public void MoveTo(Point at)
    {
        if (!_fitted) return;
        switch (_drag)
        {
            case Drag.None:
                SetHover(IndexAt(at));
                return;
            case Drag.Pending:
                if (!BandSelect) return;
                if (Math.Abs(at.X - _down.X) <= DragPixels && Math.Abs(at.Y - _down.Y) <= DragPixels) return;
                _drag = Drag.Band;
                SetHover(-1);
                goto case Drag.Band;
            case Drag.Band:
                ShowBand(at);
                break;
            case Drag.Pan:
                Camera.Pan(at.X - _last.X, at.Y - _last.Y);
                SyncCamera();
                break;
            case Drag.Orbit:
                Camera.Orbit(at.X - _last.X, at.Y - _last.Y, _pivot);
                SyncCamera();
                break;
        }
        _last = at;
    }

    /// <summary>단추를 뗐다.</summary>
    public void Release(Point at) => Finish(EndDrag(), at);

    // 끌기를 끝난 것으로 적고, 하던 것(무엇을 · 어디서 눌렀나 · Ctrl)을 돌려준다.
    private (Drag drag, Point down, bool ctrl) EndDrag()
    {
        var ended = (_drag, _down, _downCtrl);
        _drag = Drag.None;
        _band.Visibility = Visibility.Collapsed;
        Cursor = _hover >= 0 ? Cursors.Hand : Cursors.Arrow;
        return ended;
    }

    // 끝난 끌기를 알린다: 움직이지 않았으면 찍기, 네모를 끌었으면 그 안의 부재들. 돌리기 · 옮기기는 끄는 동안 이미 되어 있다.
    private void Finish((Drag drag, Point down, bool ctrl) ended, Point at)
    {
        if (ended.drag == Drag.Pending)
        {
            ClickPoint = ended.down;
            Clicked?.Invoke(IndexAt(ended.down), ended.ctrl);
        }
        else if (ended.drag == Drag.Band)
        {
            // 왼쪽에서 오른쪽으로 끌면 온전히 든 것만, 오른쪽에서 왼쪽으로 끌면 걸친 것까지.
            bool crossing = at.X < ended.down.X;
            var hit = Picker.InRect(_meshes, i => _bodyModel[i] != null && Shown(i), Camera, ended.down.X, ended.down.Y, at.X, at.Y, crossing);
            Banded?.Invoke(hit, ended.ctrl);
        }
    }

    /// <summary>굴렸다(위로 = 가까이). 마우스 밑의 자리가 제자리에 머문다.</summary>
    public void Wheel(Point at, int delta)
    {
        if (!_fitted || delta == 0) return;
        Camera.ZoomAt(at.X, at.Y, Math.Pow(1.2, delta / 120.0));
        SyncCamera();
    }

    private void ShowBand(Point at)
    {
        bool crossing = at.X < _down.X;
        var color = crossing ? Color.FromRgb(52, 168, 83) : Color.FromRgb(66, 133, 244);
        _band.Stroke = Brush(color);
        _band.StrokeDashArray = crossing ? new DoubleCollection { 4, 3 } : null;
        _band.Fill = Brush(Color.FromArgb(28, color.R, color.G, color.B));
        Canvas.SetLeft(_band, Math.Min(_down.X, at.X));
        Canvas.SetTop(_band, Math.Min(_down.Y, at.Y));
        _band.Width = Math.Abs(at.X - _down.X);
        _band.Height = Math.Abs(at.Y - _down.Y);
        _band.Visibility = Visibility.Visible;
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        int was = _hover;
        _hover = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Arrow;
        // 마우스 밑이 바뀐 두 것의 색만 다시 칠한다 — 그림 전체를 다시 짓지 않게(② 는 부재 + 판이 수백 장이다 · R48).
        Repaint(was);
        Repaint(index);
        Hovered?.Invoke(index);
    }

    // 그 차례의 색만 지금 값(마우스 밑이면 밝게)으로 다시 칠한다. 그 차례가 어느 층에 들었는지 · 보이는지는 그대로다(그것은 Refresh 가 정한다).
    private void Repaint(int i)
    {
        if (i < 0 || i >= _meshes.Count || _bodyModel[i] is not { } body) return;
        var color = ColorOf(i);
        if (i == _hover) color = Lighter(color);
        if (!_layered) { body.Material = body.BackMaterial = MaterialOf(color); return; }
        var style = StyleOf(i);
        if (style == MeshStyle.Ghost) return;
        body.Material = body.BackMaterial = MaterialOf(color);
        if (style is MeshStyle.Tint or MeshStyle.Top && _edgeModel[i] is { Geometry: not null } edge) edge.Material = edge.BackMaterial = FlatOf(Darker(color));
    }
}
