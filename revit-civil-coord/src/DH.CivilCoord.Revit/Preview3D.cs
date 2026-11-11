using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using DH.CivilCoord.Core;

namespace DH.CivilCoord.Revit;

/// <summary>
/// 3D 미리보기(JACK 1111 «Civil 이 박스로 내보낸 영역을 3D로 · Revit 도 3D로»): Civil 최종지표면 · 3D 선(옹벽 · 소단 · 경계 …) + 지금 맞춤으로 옮긴 Revit 구조물 입체.
/// <para>조작(인터뷰 1111): 구조물 끌기 = 땅 위에서 옮기기(높이 그대로) · 둥근 손잡이 = 돌리기(Shift = 1° 단위) · 위아래 손잡이 = 높이(«더할 값» 칸 · 1 cm 단위) ·
/// 오른쪽 끌기 · Shift+휠 눌러 끌기 = 돌려 보기 · 휠 눌러 끌기 · 빈 곳 끌기 = 화면 이동 · 휠 = 확대/축소 · 두 번 누르기 = 전체 보기 · 끄는 동안 Esc = 취소.</para>
/// <para>그림: 평행 투영(Revit 기본 3D 와 같은 꼴). Revit 은 WPF 를 소프트웨어로만 그려(세모 30만 = 한 장 0.36 s — 1111 잼) 돌리거나 끄는 동안은
/// 지표면을 성긴 격자(<see cref="TerrainLod"/>)로 바꿔 그리고 손을 떼면 원래 세모로. 3D 선 · 점 · 손잡이는 위에 겹친 2D 그림(가려지지 않음 — 미리보기라 괜찮음).
/// 도형 좌표는 장면 원점(영역 가운데 · 가장 낮은 높이)을 뺀 작은 수.</para>
/// </summary>
internal sealed class Preview3D : Border
{
    public event Action<Fit2D, bool>? HandChanged;
    /// <summary>«더할 값»(m)이 바뀜 — 위아래 손잡이(1 cm 단위) · PageUp/PageDown. null = 끌기 취소(칸을 끌기 전 글로 되돌림).</summary>
    public event Action<double?, bool>? DzChanged;

    private readonly UiTheme _ui;
    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Viewport3D _vp = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _info;
    private readonly OrthographicCamera _cam = new();
    private readonly Model3DGroup _scene = new();
    private readonly Model3DGroup _structGroup = new();
    private readonly MatrixTransform3D _structT = new();
    private Model3DGroup? _terrainFull, _terrainCoarse;
    private readonly List<GeometryModel3D> _terrainModels = new();
    private readonly DispatcherTimer _settle;

    // 자료
    private Region3D? _region;
    private PreviewModel? _model;
    private Fit2D? _fit;
    private double _tz, _dz;
    private (double X, double Y, double Z)? _c1, _c2;
    private Point3D _o;                  // 장면 원점(세계)
    private bool _originSet;
    private P2 _l0;                      // 구조물 가운데(Revit 내부 m)
    private double _lTop, _zRef, _zTop;  // 윤곽 상자 위 끝(Y) · 가장 낮은 · 높은 Z(Revit 내부 m)
    private double _lx0, _lx1, _ly0;
    private bool _hasStruct;
    private int _terrainTris;
    private bool _translucent;

    // 카메라: 바라보는 점(장면 좌표) · 방위(도 · 동=0 반시계) · 내려다보는 각(도) · 보이는 폭(m)
    private Point3D _target;
    private double _yaw = 135, _pitch = 32, _width = 100;
    private bool _viewSet;
    private static (Point3D Target, double Yaw, double Pitch, double Width, Point3D Origin)? s_view;

    // 끌기
    private enum Drag { None, Move, Rotate, Height, Orbit, Pan }
    private Drag _drag;
    private MouseButton _dragBtn;
    private Point _p0;
    private Fit2D? _fit0;
    private double _dz0, _yaw0, _pitch0, _tzLive;
    private Point3D _target0;
    private P2 _pivot0;
    private Point3D? _ground0;
    private Point? _rotHandle, _hHandle;
    private bool _moved;   // 이번 끌기가 문턱을 넘었나

    public Preview3D(UiTheme ui)
    {
        _ui = ui;
        Background = ui["Card"];
        BorderBrush = ui["Line"];
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        _vp.Camera = _cam;
        // 빛: 북서쪽 위에서 비스듬히(지형 음영처럼 — 사면 · 옹벽 단차가 보이게) + 반대쪽 약한 빛
        _scene.Children.Add(new AmbientLight(Color.FromRgb(78, 78, 78)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(215, 215, 210), new Vector3D(0.45, -0.55, -0.6)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(60, 60, 66), new Vector3D(-0.5, 0.4, -0.4)));
        _structGroup.Transform = _structT;
        _scene.Children.Add(_structGroup);
        _vp.Children.Add(new ModelVisual3D { Content = _scene });
        _root.Children.Add(_vp);
        _root.Children.Add(_overlay);
        _info = new TextBlock { Text = "", FontFamily = UiTheme.IconFont, FontSize = 14, Foreground = ui["Sub"], Cursor = Cursors.Help, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 6) };
        ui.Tip(_info, "구조물 끌기 = 땅 위에서 옮기기 · 둥근 손잡이 = 돌리기(Shift = 1° 단위) · 위아래 손잡이 = 높이(1 cm 단위)\n" +
                      "오른쪽 끌기 또는 Shift+휠 눌러 끌기 = 돌려 보기 · 휠 눌러 끌기 · 빈 곳 끌기 = 화면 이동 · 휠 = 확대/축소 · 두 번 누르기 = 전체 보기\n" +
                      "방향키 = 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01° 돌림 · PageUp/PageDown = 높이 1 cm\n" +
                      "초록 면 = Civil 지표면 · 파란 입체 = Revit 구조물 · 선 = Civil 3D 선 · 1 · 2 = Civil 두 점");
        _root.Children.Add(_info);
        Child = _root;
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _settle.Tick += (_, _) => { _settle.Stop(); if (_drag == Drag.None) Detail(true); };
        SizeChanged += (_, _) => Render();
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
    }

    public bool Translucent
    {
        get => _translucent;
        set { if (_translucent == value) return; _translucent = value; ApplyTerrainMaterial(); }
    }

    /// <summary>그릴 것. tz = 레벨 높이 맞춤(Revit 내부 Z + tz = Civil 표고) · dz = 지금 «더할 값»(tz 에 든 것).</summary>
    public void Show(Region3D? region, PreviewModel model, Fit2D? fit, double tz, double dz,
                     (double X, double Y, double Z)? c1, (double X, double Y, double Z)? c2)
    {
        if (_drag != Drag.None) return;   // 끄는 동안 바깥에서 다시 그리기 — 끄는 쪽이 이김
        bool regionNew = !ReferenceEquals(region, _region), modelNew = !ReferenceEquals(model, _model);
        bool same = !regionNew && !modelNew && SameFit(fit, _fit) && Math.Abs(tz - _tz) < 1e-9 && c1 == _c1 && c2 == _c2;
        _dz = dz;
        if (same) return;   // 값이 같으면 다시 그리지 않음(소프트웨어 그리기는 비쌈)
        bool hadFit = _fit != null;
        _region = region; _model = model; _fit = fit; _tz = tz; _c1 = c1; _c2 = c2;
        if (regionNew || !_originSet)
        {
            var old = _o; bool had = _originSet;
            SetOrigin();
            if (had && _viewSet) _target += old - _o;   // 원점이 바뀌어도 보던 자리 그대로
        }
        if (regionNew) BuildTerrain();
        if (modelNew || regionNew) BuildStruct();
        if (_viewSet && W > 40 && fit != null && !hadFit) FitView();
        Render();
    }

    private static bool SameFit(Fit2D? a, Fit2D? b)
    {
        if (a == null || b == null) return a == null && b == null;
        var p = a.Anchor; var q = b.Anchor;
        return p.X == q.X && p.Y == q.Y && a.Angle == b.Angle && a.Scale == b.Scale && a.R1 == b.R1 && a.R2 == b.R2;
    }

    /// <summary>끄는 중이면 처음 자리로 되돌리고 true.</summary>
    public bool CancelDrag()
    {
        if (_drag == Drag.None) return false;
        var d = _drag;
        _drag = Drag.None;
        if (d is Drag.Move or Drag.Rotate && _fit0 != null) { _fit = _fit0; HandChanged?.Invoke(_fit0, true); }
        else if (d == Drag.Height) { _tz = _tzLive; _dz = _dz0; DzChanged?.Invoke(null, true); }
        else if (d == Drag.Orbit) { _yaw = _yaw0; _pitch = _pitch0; }
        else if (d == Drag.Pan) _target = _target0;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Detail(true);
        Render();
        return true;
    }

    // ── 장면 ─────────────────────────────────────────────────────────────────

    private void SetOrigin()
    {
        if (_region?.Bounds() is { } b) _o = new Point3D(Math.Round((b.X0 + b.X1) / 2), Math.Round((b.Y0 + b.Y1) / 2), Math.Floor(b.Z0));
        else if (_fit != null) { var a = _fit.Anchor; _o = new Point3D(Math.Round(a.X), Math.Round(a.Y), Math.Floor(_tz)); }
        else if (_c1 is { } c) _o = new Point3D(Math.Round(c.X), Math.Round(c.Y), Math.Floor(c.Z));
        else return;
        _originSet = true;
    }

    private Color TerrainColor(Region3D.Mesh m) =>
        m.Color is { } c ? Color.FromRgb(c.R, c.G, c.B) : m.Kind switch
        {
            "ground" => Color.FromRgb(176, 160, 128),
            "road" => Color.FromRgb(150, 150, 150),
            _ => Color.FromRgb(150, 178, 128),
        };

    private void BuildTerrain()
    {
        if (_terrainFull != null) _scene.Children.Remove(_terrainFull);
        if (_terrainCoarse != null) _scene.Children.Remove(_terrainCoarse);
        _terrainFull = _terrainCoarse = null;
        _terrainModels.Clear();
        _terrainTris = 0;
        if (_region == null || _region.Meshes.Count == 0) return;
        _terrainFull = new Model3DGroup();
        foreach (var m in _region.Meshes)
        {
            _terrainTris += m.TriangleCount;
            foreach (var g in Chunks(m.V, m.T))
            {
                var gm = new GeometryModel3D { Geometry = g, Material = TerrainMaterial(TerrainColor(m)), BackMaterial = TerrainMaterial(Color.FromRgb(128, 128, 128)) };
                _terrainModels.Add(gm);
                _terrainFull.Children.Add(gm);
            }
        }
        _scene.Children.Add(_terrainFull);   // 구조물 다음(반투명일 때 뒤에 그려야 비침)
    }

    /// <summary>성긴 격자(돌리거나 끄는 동안) — 처음 쓸 때 만듦.</summary>
    private Model3DGroup? Coarse()
    {
        if (_terrainCoarse != null || _region == null) return _terrainCoarse;
        _terrainCoarse = new Model3DGroup();
        foreach (var m in _region.Meshes)
        {
            int share = Math.Max(500, (int)(CoarseTris * (double)m.TriangleCount / Math.Max(1, _terrainTris)));
            var (v, t) = m.TriangleCount > share ? TerrainLod.Coarse(m, share) : (m.V, m.T);
            foreach (var g in Chunks(v, t))
            {
                var gm = new GeometryModel3D { Geometry = g, Material = TerrainMaterial(TerrainColor(m)), BackMaterial = TerrainMaterial(Color.FromRgb(128, 128, 128)) };
                _terrainModels.Add(gm);
                _terrainCoarse.Children.Add(gm);
            }
        }
        return _terrainCoarse;
    }

    private const int CoarseTris = 30_000, CoarseOver = 45_000;

    /// <summary>자세히(true) / 성기게(false) 지표면.</summary>
    private void Detail(bool full)
    {
        if (_terrainFull == null || _terrainTris <= CoarseOver) return;
        var want = full ? _terrainFull : Coarse();
        if (want == null || _scene.Children.Contains(want)) return;
        _scene.Children.Remove(full ? _terrainCoarse! : _terrainFull);
        _scene.Children.Add(want);
    }

    private Material TerrainMaterial(Color c)
    {
        var brush = new SolidColorBrush(c) { Opacity = _translucent ? 0.4 : 1 };
        brush.Freeze();
        var mat = new DiffuseMaterial(brush);
        mat.Freeze();
        return mat;
    }

    private void ApplyTerrainMaterial()
    {
        if (_region == null) return;
        // 색은 만든 때의 것을 다시 씀(브러시 불투명도만 바꿈)
        foreach (var gm in _terrainModels)
        {
            if (gm.Material is DiffuseMaterial { Brush: SolidColorBrush b }) gm.Material = TerrainMaterial(b.Color);
            if (gm.BackMaterial is DiffuseMaterial { Brush: SolidColorBrush bb }) gm.BackMaterial = TerrainMaterial(bb.Color);
        }
        Render();
    }

    /// <summary>세모 → 면마다 따로 꼭짓점(평평한 음영) · 10만 세모씩 나눔. 좌표는 장면 원점을 뺌.</summary>
    private IEnumerable<MeshGeometry3D> Chunks(double[] v, int[] t)
    {
        const int per = 100_000;
        for (int s = 0; s < t.Length / 3; s += per)
        {
            int n = Math.Min(per, t.Length / 3 - s);
            var pos = new Point3DCollection(n * 3);
            var nor = new Vector3DCollection(n * 3);
            var idx = new Int32Collection(n * 3);
            for (int k = 0; k < n; k++)
            {
                int a = t[(s + k) * 3] * 3, b = t[(s + k) * 3 + 1] * 3, c = t[(s + k) * 3 + 2] * 3;
                var pa = new Point3D(v[a] - _o.X, v[a + 1] - _o.Y, v[a + 2] - _o.Z);
                var pb = new Point3D(v[b] - _o.X, v[b + 1] - _o.Y, v[b + 2] - _o.Z);
                var pc = new Point3D(v[c] - _o.X, v[c + 1] - _o.Y, v[c + 2] - _o.Z);
                var nn = Vector3D.CrossProduct(pb - pa, pc - pa);
                if (nn.LengthSquared < 1e-18) continue;
                nn.Normalize();
                if (nn.Z < 0) nn.Negate();   // 지표면은 위를 보게(감기가 틀려도 밝게)
                int i = pos.Count;
                pos.Add(pa); pos.Add(pb); pos.Add(pc);
                nor.Add(nn); nor.Add(nn); nor.Add(nn);
                idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
            }
            var g = new MeshGeometry3D { Positions = pos, Normals = nor, TriangleIndices = idx };
            g.Freeze();
            yield return g;
        }
    }

    private void BuildStruct()
    {
        _structGroup.Children.Clear();
        _hasStruct = false;
        var shells = _model?.Shells ?? new List<MeshShell>();
        var all = shells.SelectMany(s => s.V).ToList();
        if (all.Count == 0) return;
        double x0 = all.Min(p => p.X), x1 = all.Max(p => p.X), y0 = all.Min(p => p.Y), y1 = all.Max(p => p.Y);
        _l0 = new P2((x0 + x1) / 2, (y0 + y1) / 2);
        _lTop = y1; _ly0 = y0; _lx0 = x0; _lx1 = x1;
        _zRef = all.Min(p => p.Z); _zTop = all.Max(p => p.Z);
        var accent = (_ui["Accent"] as SolidColorBrush)?.Color ?? Color.FromRgb(37, 99, 235);
        var front = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb((byte)((accent.R + 255) / 2), (byte)((accent.G + 255) / 2), (byte)((accent.B + 255) / 2))));
        front.Freeze();
        // 꼭짓점 묶음(평평한 음영) — 10만 세모씩
        var pos = new Point3DCollection(); var nor = new Vector3DCollection(); var idx = new Int32Collection();
        void Flush()
        {
            if (idx.Count == 0) return;
            var g = new MeshGeometry3D { Positions = pos, Normals = nor, TriangleIndices = idx };
            g.Freeze();
            _structGroup.Children.Add(new GeometryModel3D { Geometry = g, Material = front, BackMaterial = front });
            pos = new Point3DCollection(); nor = new Vector3DCollection(); idx = new Int32Collection();
        }
        foreach (var sh in shells)
            foreach (var (a, b, c) in sh.T)
            {
                if ((uint)a >= sh.V.Count || (uint)b >= sh.V.Count || (uint)c >= sh.V.Count) continue;
                var pa = L(sh.V[a]); var pb = L(sh.V[b]); var pc = L(sh.V[c]);
                var nn = Vector3D.CrossProduct(pb - pa, pc - pa);
                if (nn.LengthSquared < 1e-18) continue;
                nn.Normalize();
                int i = pos.Count;
                pos.Add(pa); pos.Add(pb); pos.Add(pc);
                nor.Add(nn); nor.Add(nn); nor.Add(nn);
                idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
                if (idx.Count >= 300_000) Flush();
            }
        Flush();
        _hasStruct = _structGroup.Children.Count > 0;
        Point3D L((double X, double Y, double Z) p) => new(p.X - _l0.X, p.Y - _l0.Y, p.Z - _zRef);
    }

    // ── 보기 ─────────────────────────────────────────────────────────────────

    private double W => _vp.ActualWidth;
    private double H => _vp.ActualHeight;
    private double Ppm => W / _width;

    private Vector3D Fwd
    {
        get
        {
            double y = _yaw * Math.PI / 180, p = _pitch * Math.PI / 180;
            return new Vector3D(Math.Cos(p) * Math.Cos(y), Math.Cos(p) * Math.Sin(y), -Math.Sin(p));
        }
    }

    private (Vector3D F, Vector3D R, Vector3D U) Basis()
    {
        var f = Fwd;
        var r = Vector3D.CrossProduct(f, new Vector3D(0, 0, 1)); r.Normalize();
        var u = Vector3D.CrossProduct(r, f); u.Normalize();
        return (f, r, u);
    }

    /// <summary>장면 점 → 화면 px.</summary>
    private Point Proj(Point3D p)
    {
        var (_, r, u) = Basis();
        var d = p - _target;
        return new Point(W / 2 + Vector3D.DotProduct(d, r) * Ppm, H / 2 - Vector3D.DotProduct(d, u) * Ppm);
    }

    /// <summary>세계 점 → 장면 점.</summary>
    private Point3D Sc(double x, double y, double z) => new(x - _o.X, y - _o.Y, z - _o.Z);

    /// <summary>화면 점에서 나가는 빛줄기를 높이 z(장면)의 수평면과 만나게 함. 거의 수평으로 볼 때는 null.</summary>
    private Point3D? Ground(Point s, double z)
    {
        var (f, r, u) = Basis();
        var o = _target + r * ((s.X - W / 2) / Ppm) + u * ((H / 2 - s.Y) / Ppm);
        if (Math.Abs(f.Z) < 0.05) return null;
        double t = (z - o.Z) / f.Z;
        return o + f * t;
    }

    private void UpdateCamera()
    {
        var f = Fwd;
        const double dist = 20000;
        _cam.Position = _target - f * dist;
        _cam.LookDirection = f * dist;
        _cam.UpDirection = new Vector3D(0, 0, 1);
        _cam.Width = _width;
        _cam.NearPlaneDistance = 1;
        _cam.FarPlaneDistance = dist * 2;
        // 구조물: 맞춤이 없으면 장면에서 뺌 · 있으면 빛 다음 · 지표면 앞(반투명 지표면이 뒤에 그려져야 비침)
        bool show = _fit != null && _hasStruct;
        if (!show) _scene.Children.Remove(_structGroup);
        else
        {
            _structT.Matrix = StructMatrix(_fit!, _tz);
            if (!_scene.Children.Contains(_structGroup)) _scene.Children.Insert(3, _structGroup);
        }
    }

    private Matrix3D StructMatrix(Fit2D fit, double tz)
    {
        double k = fit.Scale, c = Math.Cos(fit.Angle), s = Math.Sin(fit.Angle);
        var w0 = fit.Apply(_l0);
        return new Matrix3D(k * c, k * s, 0, 0, -k * s, k * c, 0, 0, 0, 0, 1, 0,
                            w0.X - _o.X, w0.Y - _o.Y, _zRef + tz - _o.Z, 1);
    }

    /// <summary>볼 것 전체(지표면 · 3D 선 · 구조물 · Civil 두 점)의 장면 범위.</summary>
    private Rect3D? SceneBox()
    {
        var pts = new List<Point3D>();
        if (_region?.Bounds() is { } b) { pts.Add(Sc(b.X0, b.Y0, b.Z0)); pts.Add(Sc(b.X1, b.Y1, b.Z1)); }
        if (_fit != null && _hasStruct) pts.AddRange(StructCorners(_fit, _tz));
        foreach (var c in new[] { _c1, _c2 }) if (c is { } q) pts.Add(Sc(q.X, q.Y, q.Z));
        if (pts.Count == 0) return null;
        double x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y), z0 = pts.Min(p => p.Z);
        return new Rect3D(x0, y0, z0, pts.Max(p => p.X) - x0, pts.Max(p => p.Y) - y0, pts.Max(p => p.Z) - z0);
    }

    /// <summary>구조물 윤곽 상자 여덟 모서리(장면 좌표).</summary>
    private IEnumerable<Point3D> StructCorners(Fit2D fit, double tz)
    {
        foreach (double x in new[] { _lx0, _lx1 })
            foreach (double y in new[] { _ly0, _lTop })
            {
                var w = fit.Apply(new P2(x, y));
                yield return Sc(w.X, w.Y, _zRef + tz);
                yield return Sc(w.X, w.Y, _zTop + tz);
            }
    }

    private void FitView()
    {
        if (SceneBox() is not { } box || W < 40) return;
        _target = new Point3D(box.X + box.SizeX / 2, box.Y + box.SizeY / 2, box.Z + box.SizeZ / 2);
        double diag = Math.Sqrt(box.SizeX * box.SizeX + box.SizeY * box.SizeY + box.SizeZ * box.SizeZ);
        _width = Math.Max(20, diag * 1.15 * Math.Max(1, W / Math.Max(1, H)));
        _viewSet = true;
        SaveView();
    }

    private void InitView()
    {
        if (s_view is { } v && v.Origin == _o && _originSet)
        {
            (_target, _yaw, _pitch, _width) = (v.Target, v.Yaw, v.Pitch, v.Width);
            _viewSet = true;
            return;
        }
        FitView();
    }

    private void SaveView() { if (_viewSet) s_view = (_target, _yaw, _pitch, _width, _o); }

    // ── 그리기(겹친 2D: 3D 선 · 점 · 손잡이 · 값) ───────────────────────────

    private void Render()
    {
        _overlay.Children.Clear();
        _rotHandle = _hHandle = null;
        double w = W, h = H;
        if (w < 40 || h < 40 || !IsVisible) return;
        if (!_viewSet) InitView();
        if (!_viewSet)
        {
            var t = new TextBlock { Text = "3D — Civil 3D 영역 · 구조물 없음", Foreground = _ui["Sub"] };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, (w - t.DesiredSize.Width) / 2); Canvas.SetTop(t, (h - t.DesiredSize.Height) / 2);
            _overlay.Children.Add(t);
            return;
        }
        UpdateCamera();

        // Civil 3D 선 — 종류별 한 Path
        if (_region != null)
            foreach (var g in _region.Lines.GroupBy(l => l.Kind))
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                    foreach (var line in g)
                    {
                        ctx.BeginFigure(Proj(Sc(line.Pts[0].X, line.Pts[0].Y, line.Pts[0].Z)), false, line.Closed);
                        ctx.PolyLineTo(line.Pts.Skip(1).Select(p => Proj(Sc(p.X, p.Y, p.Z))).ToList(), true, false);
                    }
                geo.Freeze();
                var (brush, width, opacity) = g.Key switch
                {
                    "site" => ("Text", 1.3, 0.85),
                    "wall" => ("WarnFg", 1.8, 0.95),
                    "road" => ("NeutralFg", 1.1, 0.8),
                    "struct" => ("BadFg", 1.3, 0.9),
                    "slope" => ("Sub", 0.9, 0.6),
                    "bench" or "contour" => ("Sub", 0.8, 0.5),
                    _ => ("Sub", 0.9, 0.7),
                };
                _overlay.Children.Add(new Path { Data = geo, Stroke = _ui[brush], StrokeThickness = width, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round });
            }

        // Civil 두 점
        void Mark((double X, double Y, double Z)? p, string label)
        {
            if (p is not { } q) return;
            var pt = Proj(Sc(q.X, q.Y, q.Z));
            var dot = new Ellipse { Width = 9, Height = 9, Fill = _ui["BadFg"], Stroke = _ui["Card"], StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, pt.X - 4.5); Canvas.SetTop(dot, pt.Y - 4.5);
            _overlay.Children.Add(dot);
            var t = new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = _ui["BadFg"] };
            Canvas.SetLeft(t, pt.X + 6); Canvas.SetTop(t, pt.Y - 17);
            _overlay.Children.Add(t);
        }
        Mark(_c1, "1");
        Mark(_c2, "2");

        if (_fit != null && _hasStruct) DrawHandles();

        // 끄는 동안 값(왼쪽 위)
        string chip = _drag switch
        {
            Drag.Move when _fit != null => $"동 {_fit.Hand.X:+0.000;-0.000;0.000} · 북 {_fit.Hand.Y:+0.000;-0.000;0.000} m",
            Drag.Rotate when _fit != null => $"돌림 {_fit.HandAngle * 180 / Math.PI:+0.00;-0.00;0.00}°",
            Drag.Height => $"더할 높이 {_dz:+0.00;-0.00;0.00} m",
            _ => "",
        };
        if (chip.Length > 0)
        {
            var c = new Border
            {
                Background = _ui["NeutralBg"], CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
                Child = new TextBlock { Text = chip, Foreground = _ui["NeutralFg"], FontSize = 12 },
            };
            Canvas.SetLeft(c, 8); Canvas.SetTop(c, 8);
            _overlay.Children.Add(c);
        }

        // 방위(위 오른쪽): 화면에서 북쪽 방향 화살표
        var (_, r, u) = Basis();
        var north = new Vector(Vector3D.DotProduct(new Vector3D(0, 1, 0), r), -Vector3D.DotProduct(new Vector3D(0, 1, 0), u));
        if (north.Length > 1e-3)
        {
            double len = Math.Min(1, north.Length);   // 내려다볼수록 화살표가 길게(수평에 가까우면 짧게)
            north.Normalize();
            var cpt = new Point(w - 26, 28);
            var tip = cpt + north * (10 * len);
            var side = new Vector(-north.Y, north.X);
            _overlay.Children.Add(new Line { X1 = cpt.X - north.X * 10 * len, Y1 = cpt.Y - north.Y * 10 * len, X2 = tip.X, Y2 = tip.Y, Stroke = _ui["Sub"], StrokeThickness = 1.5 });
            _overlay.Children.Add(new Polygon { Points = { tip + north * 3, tip - north * 4 + side * 4, tip - north * 4 - side * 4 }, Fill = _ui["Sub"] });
            var n = new TextBlock { Text = "N", FontSize = 10, Foreground = _ui["Sub"] };
            Canvas.SetLeft(n, tip.X + north.X * 9 - 4); Canvas.SetTop(n, tip.Y + north.Y * 9 - 7);
            _overlay.Children.Add(n);
        }
    }

    /// <summary>돌림 손잡이(구조물 윤곽 상자 위 끝 가운데 — 구조물 «위»(Revit +Y) 쪽 26 px 바깥) · 높이 손잡이(구조물 꼭대기 가운데 위 36 px).</summary>
    private void DrawHandles()
    {
        var fit = _fit!;
        double tz = _tz;
        var pc = fit.Apply(_l0); var pt = fit.Apply(new P2(_l0.X, _lTop));
        var pivot = Proj(Sc(pc.X, pc.Y, _zRef + tz));
        var top = Proj(Sc(pt.X, pt.Y, _zRef + tz));
        var upW = fit.Apply(new P2(_l0.X, _l0.Y + 1));
        var up = Proj(Sc(upW.X, upW.Y, _zRef + tz)) - pivot;
        if (up.Length > 1e-6)
        {
            up.Normalize();
            var hp = top + up * 26;
            _rotHandle = hp;
            _overlay.Children.Add(new Line { X1 = top.X, Y1 = top.Y, X2 = hp.X, Y2 = hp.Y, Stroke = _ui["Accent"], StrokeThickness = 1.2 });
            Knob(hp, "");
        }
        var crown = Proj(Sc(pc.X, pc.Y, _zTop + tz));
        var hh = crown + new Vector(0, -36);
        // 두 손잡이가 겹치면(보는 방향에 따라) 높이 손잡이를 위로 비켜 둠 — 누르면 돌림이 먼저 잡히므로
        if (_rotHandle is Point rp && (hh - rp).Length < 26)
            hh = new Point(hh.X, Math.Min(hh.Y, rp.Y) - 26);
        _hHandle = hh;
        _overlay.Children.Add(new Line { X1 = crown.X, Y1 = crown.Y, X2 = hh.X, Y2 = hh.Y, Stroke = _ui["Accent"], StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 2, 2 } });
        Knob(hh, "");
        if (_drag is Drag.Rotate)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = _ui["Accent"] };
            Canvas.SetLeft(dot, pivot.X - 3); Canvas.SetTop(dot, pivot.Y - 3);
            _overlay.Children.Add(dot);
        }
    }

    private void Knob(Point at, string glyph)
    {
        var ring = new Ellipse { Width = 18, Height = 18, Fill = _ui["Card"], Stroke = _ui["Accent"], StrokeThickness = 1.8 };
        Canvas.SetLeft(ring, at.X - 9); Canvas.SetTop(ring, at.Y - 9);
        _overlay.Children.Add(ring);
        var g = new TextBlock { Text = glyph, FontFamily = UiTheme.IconFont, FontSize = 10, Foreground = _ui["Accent"] };
        g.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(g, at.X - g.DesiredSize.Width / 2); Canvas.SetTop(g, at.Y - g.DesiredSize.Height / 2);
        _overlay.Children.Add(g);
    }

    // ── 마우스 · 글쇠 ────────────────────────────────────────────────────────

    private static bool Near(Point? a, Point p) => a is Point q && (p - q).Length <= 11;

    /// <summary>화면에서 구조물 모양(윤곽 상자 여덟 모서리의 볼록 껍질) 안인가 — 지표면에 묻혀 있어도 잡히게 3D 맞히기 대신.</summary>
    private bool OnStruct(Point p)
    {
        if (_fit == null || !_hasStruct) return false;
        var hull = Hull.Convex(StructCorners(_fit, _tz).Select(c => { var s = Proj(c); return new P2(s.X, s.Y); }));
        if (hull.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = hull.Count - 1; i < hull.Count; j = i++)
            if ((hull[i].Y > p.Y) != (hull[j].Y > p.Y) &&
                p.X < (hull[j].X - hull[i].X) * (p.Y - hull[i].Y) / (hull[j].Y - hull[i].Y) + hull[i].X)
                inside = !inside;
        return inside;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_drag != Drag.None) { e.Handled = true; return; }
        Focus();
        if (Press(e.GetPosition(_vp), e.ChangedButton, e.ClickCount, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { if (_drag != Drag.None) CaptureMouse(); e.Handled = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        DragTo(e.GetPosition(_vp), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (Release(e.ChangedButton)) e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag != Drag.None) Finish();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Wheel(e.GetPosition(_vp), e.Delta)) e.Handled = true;
    }

    // 오른쪽 단추를 뗄 때 바탕 메뉴가 뜨지 않게
    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (_drag == Drag.Orbit && _dragBtn == MouseButton.Right) { Release(MouseButton.Right); e.Handled = true; }
    }

    internal bool Press(Point p, MouseButton button, int clicks, bool shift)
    {
        if (_drag != Drag.None || !_viewSet) return false;
        if (button == MouseButton.Left && clicks == 2) { FitView(); Render(); return true; }
        var mode = button switch
        {
            MouseButton.Left => Near(_rotHandle, p) ? Drag.Rotate : Near(_hHandle, p) ? Drag.Height : OnStruct(p) ? Drag.Move : Drag.Pan,
            MouseButton.Right => Drag.Orbit,
            MouseButton.Middle => shift ? Drag.Orbit : Drag.Pan,
            _ => Drag.None,
        };
        if (mode == Drag.None) return false;
        _drag = mode; _dragBtn = button; _p0 = p; _fit0 = _fit; _dz0 = _dz; _tzLive = _tz; _moved = false;
        _yaw0 = _yaw; _pitch0 = _pitch; _target0 = _target;
        if (mode is Drag.Move or Drag.Rotate)
        {
            var c = _fit!.Apply(_l0);
            _pivot0 = c;
            _ground0 = Ground(p, _zRef + _tz - _o.Z);
            if (_ground0 == null && mode == Drag.Rotate) { _drag = Drag.None; return false; }
        }
        Cursor = mode switch { Drag.Rotate => Cursors.Hand, Drag.Move => Cursors.SizeAll, Drag.Height => Cursors.SizeNS, Drag.Orbit => Cursors.Cross, _ => Cursors.ScrollAll };
        return true;
    }

    internal void DragTo(Point p, bool shift)
    {
        if (_drag == Drag.None)
        {
            Cursor = Near(_rotHandle, p) ? Cursors.Hand : Near(_hHandle, p) ? Cursors.SizeNS : OnStruct(p) ? Cursors.SizeAll : null;
            return;
        }
        // 누른 자리에서 몇 px 넘게 끌기 전에는 값을 바꾸지 않음 — 누르기(두 번 누르기 포함)의 1 px 흔들림이 손 맞춤 · 높이로 들어가지 않게(설치 전 검토 중간)
        if (!_moved)
        {
            if (Math.Abs(p.X - _p0.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _p0.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _moved = true;
            Detail(false);   // 실제로 끌기 시작할 때 성긴 지표면으로(그냥 누르기는 다시 그리지 않음)
        }
        double dx = p.X - _p0.X, dy = p.Y - _p0.Y;
        switch (_drag)
        {
            case Drag.Orbit:
                _yaw = _yaw0 - dx * 0.4;
                _pitch = Math.Clamp(_pitch0 + dy * 0.3, -5, 89.5);
                Render();
                break;
            case Drag.Pan:
            {
                var (_, r, u) = Basis();
                _target = _target0 - r * (dx / Ppm) + u * (dy / Ppm);
                Render();
                break;
            }
            case Drag.Move:
            {
                double zp = _zRef + _tz - _o.Z;
                if (_ground0 is { } g0 && Ground(p, zp) is { } g1) Live(_fit0!.Moved(g1.X - g0.X, g1.Y - g0.Y));
                else
                {
                    // 거의 수평으로 볼 때: 화면 오른쪽 방향으로만
                    var (_, r, _) = Basis();
                    var hr = new Vector(r.X, r.Y); if (hr.Length > 1e-9) hr.Normalize();
                    Live(_fit0!.Moved(hr.X * dx / Ppm, hr.Y * dx / Ppm));
                }
                break;
            }
            case Drag.Rotate:
            {
                double zp = _zRef + _tz - _o.Z;
                if (_ground0 is not { } g0 || Ground(p, zp) is not { } g1) break;
                double px = _pivot0.X - _o.X, py = _pivot0.Y - _o.Y;
                double d = Fit2D.Normalize(Math.Atan2(g1.Y - py, g1.X - px) - Math.Atan2(g0.Y - py, g0.X - px));
                if (shift)
                {
                    double deg = Math.Round((_fit0!.HandAngle + d) * 180 / Math.PI);
                    d = deg * Math.PI / 180 - _fit0.HandAngle;
                }
                Live(_fit0!.RotatedAbout(_pivot0, d));
                break;
            }
            case Drag.Height:
            {
                var (_, _, u) = Basis();
                double zs = Math.Max(0.15, u.Z);   // 화면 위쪽 1 px 이 몇 m 높이인가(내려다볼수록 둔하게)
                double dz = Math.Round((_dz0 - dy / (Ppm * zs)) * 100) / 100;
                if (dz != _dz)
                {
                    _tz = _tzLive + (dz - _dz0);
                    _dz = dz;
                    Render();
                    DzChanged?.Invoke(dz, false);
                }
                break;
            }
        }
    }

    internal bool Release(MouseButton button)
    {
        if (_drag == Drag.None || button != _dragBtn) return false;
        Finish();
        return true;
    }

    /// <summary>끌기 끝: 끝났다고 먼저 적고 → 놓고 → 알린다.</summary>
    private void Finish()
    {
        var d = _drag;
        _drag = Drag.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = null;
        Detail(true);
        SaveView();
        Render();
        if (d is Drag.Move or Drag.Rotate && _fit != null && !ReferenceEquals(_fit, _fit0)) HandChanged?.Invoke(_fit, true);
        if (d == Drag.Height && _dz != _dz0) DzChanged?.Invoke(_dz, true);
    }

    internal bool Wheel(Point p, int delta)
    {
        if (_drag is Drag.Move or Drag.Rotate or Drag.Height || !_viewSet) return false;
        var (_, r, u) = Basis();
        double ox = (p.X - W / 2) / Ppm, oy = (H / 2 - p.Y) / Ppm;
        var under = _target + r * ox + u * oy;
        _width = Math.Clamp(_width / Math.Pow(1.2, delta / 120.0), 1, 50000);
        _target = under - r * ((p.X - W / 2) / Ppm) - u * ((H / 2 - p.Y) / Ppm);
        if (_drag == Drag.Pan) { _p0 = p; _target0 = _target; }
        Detail(false);
        _settle.Stop(); _settle.Start();   // 휠을 멈추면 자세히
        SaveView();
        Render();
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) e.Handled = true;
    }

    /// <summary>방향키 = 동 · 북 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01°(Shift 0.1°) 돌림 · PageUp/PageDown = 높이 1 cm(Shift 10 cm).</summary>
    internal bool Nudge(Key key, bool shift, bool ctrl)
    {
        if (_fit == null || !_hasStruct || _drag != Drag.None) return false;
        double step = shift ? 0.1 : 0.01, turn = (shift ? 0.1 : 0.01) * Math.PI / 180;
        if (key is Key.PageUp or Key.PageDown)
        {
            double dz = _dz + (key == Key.PageUp ? step : -step);   // 반올림하지 않음(칸의 1.234 가 1.24 로 바뀌지 않게)
            _tz += dz - _dz;
            _dz = dz;
            Render();
            DzChanged?.Invoke(dz, true);
            return true;
        }
        var pivot = _fit.Apply(_l0);
        Fit2D? f = key switch
        {
            Key.Left => ctrl ? _fit.RotatedAbout(pivot, turn) : _fit.Moved(-step, 0),
            Key.Right => ctrl ? _fit.RotatedAbout(pivot, -turn) : _fit.Moved(step, 0),
            Key.Up when !ctrl => _fit.Moved(0, step),
            Key.Down when !ctrl => _fit.Moved(0, -step),
            _ => null,
        };
        if (f == null) return false;
        _fit = f;
        Render();
        HandChanged?.Invoke(f, true);
        return true;
    }

    private void Live(Fit2D f)
    {
        _fit = f;
        Render();
        HandChanged?.Invoke(f, false);
    }

    /// <summary>시험 도구용.</summary>
    internal Point? RotHandlePoint => _rotHandle;
    internal Point? HeightHandlePoint => _hHandle;
    internal Point? CenterPoint => _fit == null || !_hasStruct ? null : Proj(Sc(_fit.Apply(_l0).X, _fit.Apply(_l0).Y, _zRef + _tz));
    internal Fit2D? CurrentFit => _fit;
    internal double CurrentDz => _dz;
    internal bool ShowingCoarse => _terrainCoarse != null && _scene.Children.Contains(_terrainCoarse);
}
