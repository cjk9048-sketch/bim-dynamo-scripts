namespace DH.Takeoff.Core.View3D;

/// <summary>미리 정해 둔 보는 방향.</summary>
public enum ViewPreset { Iso, Top, Front, Back, Left, Right }

/// <summary>
/// 3D 팝업의 카메라 — 원근 없는(직교) 그림이고 Z 가 위다. 한 점(Target)을 바라보며 그 둘레를 돈다.
///  · Yaw = 카메라가 선 방위(라디안): Target 에서 카메라 쪽을 위에서 내려다본 각. 0 = 동쪽(+X)에 서서 서쪽을 봄, −90° = 남쪽에 서서 북쪽을 봄.
///  · Pitch = 카메라의 높이각(라디안): 0 = 옆에서, +90° = 바로 위에서 내려다봄(그때 화면 위쪽 = 카메라가 보는 수평 방향).
///  · Width = 화면 가로에 들어오는 길이(m). 화면 크기(ViewW × ViewH 픽셀)는 그리는 쪽이 넣어 준다.
/// 화면 좌표는 왼쪽 위가 (0, 0)이고 아래로 갈수록 y 가 커진다.
/// </summary>
public sealed class OrbitCamera
{
    public const double MinWidth = 0.05;        // 가장 가까이(5 cm)
    public const double MaxWidth = 100_000;     // 가장 멀리(100 km)
    private const double MaxPitch = 89.999 * Math.PI / 180;

    public Vec3 Target { get; set; } = Vec3.Zero;
    public double Yaw { get; set; } = -45 * Math.PI / 180;
    public double Pitch { get; set; } = Math.Atan(1 / Math.Sqrt(2));   // 등각(35.26°)
    public double Width { get; set; } = 50;
    public double ViewW { get; set; } = 800;
    public double ViewH { get; set; } = 600;

    /// <summary>화면 1픽셀의 길이(m).</summary>
    public double PixelSize => Width / Math.Max(1, ViewW);

    /// <summary>보는 방향(카메라 → Target).</summary>
    public Vec3 Forward => new(-Math.Cos(Pitch) * Math.Cos(Yaw), -Math.Cos(Pitch) * Math.Sin(Yaw), -Math.Sin(Pitch));

    /// <summary>화면의 오른쪽(언제나 수평).</summary>
    public Vec3 Right => new(-Math.Sin(Yaw), Math.Cos(Yaw), 0);

    /// <summary>화면의 위쪽.</summary>
    public Vec3 Up => Right.Cross(Forward);

    /// <summary>점이 화면의 어디에 비치는가. depth = 보는 방향으로 Target 보다 얼마나 먼가(m, 클수록 멀다).</summary>
    public (double x, double y, double depth) Project(Vec3 p)
    {
        var d = p - Target;
        double ps = PixelSize;
        return (ViewW / 2 + d.Dot(Right) / ps, ViewH / 2 - d.Dot(Up) / ps, d.Dot(Forward));
    }

    /// <summary>화면의 한 점을 지나는 시선: Target 을 지나는 화면 평면 위의 점과 보는 방향.</summary>
    public (Vec3 origin, Vec3 direction) Ray(double x, double y)
    {
        double ps = PixelSize;
        return (Target + Right * ((x - ViewW / 2) * ps) + Up * ((ViewH / 2 - y) * ps), Forward);
    }

    /// <summary>
    /// 돌린다(마우스를 dx, dy 픽셀 끌었다). 끄는 쪽으로 모델이 따라 도는 느낌: 오른쪽으로 끌면 카메라가 왼쪽으로, 아래로 끌면 카메라가 위로.
    /// pivot 을 주면 그 점이 화면의 제자리에 머문다(마우스 밑의 부재를 축으로 도는 느낌).
    /// </summary>
    public void Orbit(double dx, double dy, Vec3? pivot = null, double degreesPerPixel = 0.4)
    {
        (double x, double y, double depth) before = pivot is { } p0 ? Project(p0) : default;
        Yaw -= dx * degreesPerPixel * Math.PI / 180;
        Pitch = Math.Clamp(Pitch + dy * degreesPerPixel * Math.PI / 180, -MaxPitch, MaxPitch);
        // 각이 끝없이 커지지 않게 −π ~ π 로 접는다.
        Yaw = Math.IEEERemainder(Yaw, 2 * Math.PI);
        if (pivot is { } p1)
        {
            var after = Project(p1);
            Pan(before.x - after.x, before.y - after.y);
        }
    }

    /// <summary>옮긴다(마우스를 dx, dy 픽셀 끌었다 — 그림이 마우스를 따라온다).</summary>
    public void Pan(double dx, double dy)
    {
        double ps = PixelSize;
        Target = Target - Right * (dx * ps) + Up * (dy * ps);
    }

    /// <summary>키우거나 줄인다(factor &gt; 1 = 가까이). 마우스 밑의 점은 제자리에 머문다.</summary>
    public void ZoomAt(double x, double y, double factor)
    {
        if (!(factor > 0) || !double.IsFinite(factor)) return;
        var (under, _) = Ray(x, y);
        double next = Math.Clamp(Width / factor, MinWidth, MaxWidth);
        Width = next;
        double ps = PixelSize;
        Target = under - Right * ((x - ViewW / 2) * ps) - Up * ((ViewH / 2 - y) * ps);
    }

    /// <summary>상자가 화면에 다 들어오게 맞춘다(보는 방향은 그대로). margin = 둘레의 여백 비율.</summary>
    public void Fit(Box3 box, double margin = 0.08)
    {
        if (box.IsEmpty) return;
        Target = box.Center;
        double halfW = 0, halfH = 0;
        Vec3 right = Right, up = Up;
        foreach (var c in box.Corners())
        {
            var d = c - Target;
            halfW = Math.Max(halfW, Math.Abs(d.Dot(right)));
            halfH = Math.Max(halfH, Math.Abs(d.Dot(up)));
        }
        double aspect = Math.Max(1, ViewW) / Math.Max(1, ViewH);
        double width = Math.Max(2 * halfW, 2 * halfH * aspect) * (1 + 2 * margin);
        Width = Math.Clamp(width > 0 ? width : 10, MinWidth, MaxWidth);
    }

    /// <summary>미리 정해 둔 방향으로 본다(바라보는 점과 크기는 그대로).</summary>
    public void Look(ViewPreset preset)
    {
        const double deg = Math.PI / 180;
        (Yaw, Pitch) = preset switch
        {
            ViewPreset.Top => (-90 * deg, MaxPitch),                       // 위에서 — 화면 위쪽이 북(+Y)
            ViewPreset.Front => (-90 * deg, 0),                            // 남쪽에서 북쪽을 봄
            ViewPreset.Back => (90 * deg, 0),
            ViewPreset.Right => (0, 0),                                    // 동쪽에서 서쪽을 봄
            ViewPreset.Left => (180 * deg, 0),
            _ => (-45 * deg, Math.Atan(1 / Math.Sqrt(2))),                 // 등각: 남동쪽 위에서
        };
    }

    /// <summary>
    /// 그리는 쪽(WPF 카메라)에 넘길 값: 카메라 자리와 가까운·먼 자르기 거리. scene = 그릴 것 전부를 싸는 상자.
    /// 가까운·먼 거리를 모델 크기에 맞춰야 깊이 판정이 촘촘하다(면 위에 살짝 띄운 모서리 띠가 깜박이지 않는다).
    /// </summary>
    public (Vec3 position, double near, double far) Placement(Box3 scene)
    {
        double reach = 1;
        if (!scene.IsEmpty)
            foreach (var c in scene.Corners()) reach = Math.Max(reach, (c - Target).Length);
        reach = reach * 1.05 + 1;
        return (Target - Forward * (2 * reach), reach, 3 * reach);
    }
}
