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

    /// <summary>
    /// 상자가 화면에 꽉 차게 맞추고(Fit — 보는 방향은 그대로 · margin = 한쪽 여백 비율 · 화면 가로가 minWidth m 아래로는 좁아지지 않게) 가운데를 focus 쪽으로 옮긴다 —
    /// 상자가 화면 밖으로 나가지 않는 만큼만(R73a6 — ② 오른쪽 «전체 모델»: 고른 부재가 꽉 차게 · 고른 면은 그 안에서 가운데 쪽으로). focus = null 이면 상자 가운데.
    /// </summary>
    public void Frame(Box3 box, Vec3? focus, double margin, double minWidth = MinWidth)
    {
        if (box.IsEmpty) return;
        Fit(box, margin);
        if (Width < minWidth) Width = Math.Min(minWidth, MaxWidth);
        if (focus is not { } f) return;
        double halfW = 0, halfH = 0;
        Vec3 right = Right, up = Up, center = box.Center;
        foreach (var c in box.Corners())
        {
            var d = c - center;
            halfW = Math.Max(halfW, Math.Abs(d.Dot(right)));
            halfH = Math.Max(halfH, Math.Abs(d.Dot(up)));
        }
        double ps = PixelSize, slackX = Math.Max(0, Math.Max(1, ViewW) / 2 * ps - halfW), slackY = Math.Max(0, Math.Max(1, ViewH) / 2 * ps - halfH);
        var shift = f - center;
        Target = center + right * Math.Clamp(shift.Dot(right), -slackX, slackX) + up * Math.Clamp(shift.Dot(up), -slackY, slackY);
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
    /// 면 하나를 보러 갈 방향(R73a — JACK 1030 «목록에서 눌러도 그 부위가 정확히 어딘지 모르겠고» · 계획 §3.6):
    /// 옆면(·경사면) = 그 면 앞쪽에서 25° 위(위를 보는 경사면은 45° · 아래를 보는 경사면은 아래 30°) · 밑면 = 아래에서 40° 비스듬히 · 윗면 = 위에서 60° ·
    /// 법선을 모르는 면(곡면) = 지금 방향 그대로. 누운 면은 방위를 지금 그대로 둔다(돌아가지 않게).
    /// </summary>
    public static (double yaw, double pitch) FacingAngles(double[]? normal, double yaw, double pitch)
    {
        const double deg = Math.PI / 180;
        if (normal is not { Length: 3 }) return (yaw, pitch);
        double nx = normal[0], ny = normal[1], nz = normal[2], flat = Math.Sqrt(nx * nx + ny * ny);
        if (flat < 0.3) return (yaw, nz < 0 ? -40 * deg : 60 * deg);
        double facing = Math.Atan2(ny, nx);   // 카메라가 그 면의 바깥(법선) 쪽에 선다 — Yaw 는 Target 에서 카메라 쪽 방위
        return (facing, nz > 0.3 ? 45 * deg : nz < -0.3 ? -30 * deg : 25 * deg);
    }

    /// <summary>
    /// 면 하나를 볼 방향 후보(R73a6 — 녹촌 전수 검사: 벽 개구부 둘레 · 계단 면은 FacingAngles(25° 위)에서 그 부재 몸에 가려 찍히지 않았다 · 56 + 6 곳):
    /// FacingAngles → 법선 그대로 → 법선에서 30° · 55° 기울인 넷씩(수평 · 수직 쪽 — 개구부 · 계단 사이로 비스듬히). 부르는 쪽이 보이는 첫 후보를 고른다.
    /// 법선을 모르면 FacingAngles 하나.
    /// </summary>
    public static IEnumerable<(double yaw, double pitch)> ViewCandidates(double[]? normal, double yaw, double pitch)
    {
        yield return FacingAngles(normal, yaw, pitch);
        if (normal is not { Length: 3 }) yield break;
        var n = new Vec3(normal[0], normal[1], normal[2]).Unit();
        var u = Vec3.UnitZ.Cross(n);
        u = u.Length < 1e-6 ? new Vec3(1, 0, 0) : u.Unit();
        var v = n.Cross(u).Unit();
        yield return AnglesOf(n);
        foreach (double deg in new[] { 30.0, 55.0 })
        {
            double t = deg * Math.PI / 180;
            foreach (var a in new[] { u, u * -1, v, v * -1 }) yield return AnglesOf(n * Math.Cos(t) + a * Math.Sin(t));
        }
    }

    /// <summary>카메라가 Target 에서 그 방향(단위)에 선 각(Yaw · Pitch — 위 · 아래 끝은 MaxPitch 로).</summary>
    public static (double yaw, double pitch) AnglesOf(Vec3 toCamera)
    {
        var d = toCamera.Unit();
        double flat = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        return (flat < 1e-9 ? -90 * Math.PI / 180 : Math.Atan2(d.Y, d.X), Math.Clamp(Math.Atan2(d.Z, flat), -MaxPitch, MaxPitch));
    }

    /// <summary>값을 떠 둔 사본(날아가기의 처음 · 끝 자리).</summary>
    public OrbitCamera Clone() => new() { Target = Target, Yaw = Yaw, Pitch = Pitch, Width = Width, ViewW = ViewW, ViewH = ViewH };

    /// <summary>
    /// from → to 의 사이 자리로 맞춘다(t = 0 ~ 1 · R73a 날아가기): 가운데는 곧게 · 크기는 비율로(가까이 갈수록 천천히 줄지 않게) · 방위는 짧은 쪽으로 돈다.
    /// 화면 크기(ViewW · ViewH)는 그대로 둔다.
    /// </summary>
    public void Blend(OrbitCamera from, OrbitCamera to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        Target = from.Target + (to.Target - from.Target) * t;
        Width = Math.Clamp(from.Width * Math.Pow(to.Width / from.Width, t), MinWidth, MaxWidth);
        Yaw = Math.IEEERemainder(from.Yaw + Math.IEEERemainder(to.Yaw - from.Yaw, 2 * Math.PI) * t, 2 * Math.PI);
        Pitch = Math.Clamp(from.Pitch + (to.Pitch - from.Pitch) * t, -MaxPitch, MaxPitch);
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
