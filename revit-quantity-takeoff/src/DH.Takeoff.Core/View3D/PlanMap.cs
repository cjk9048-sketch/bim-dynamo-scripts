namespace DH.Takeoff.Core.View3D;

/// <summary>
/// 3D 구석의 평면 지도(R73a — 계획 §3.6 «3D 구석 평면 지도 + 빨간 점»): 부재 겉면의 세모를 평면으로 눌러(위아래를 보는 세모만 — 선 세모는 넓이가 없다)
/// 정해 둔 크기(픽셀)에 맞춘다. 위 = 북(+Y — 3D 의 «위에서» 보기와 같은 북). 보기만 한다(누르지 않는다).
/// 좌표는 겉면과 같은 장면 좌표(원점을 뺀 것)다.
/// </summary>
public sealed class PlanMap
{
    public double Width { get; }
    public double Height { get; }

    /// <summary>1 m 가 몇 픽셀인가.</summary>
    public double Scale { get; }

    private readonly double _minX, _maxY, _offX, _offY;

    private PlanMap(double width, double height, double scale, double minX, double maxY, double offX, double offY)
    {
        Width = width;
        Height = height;
        Scale = scale;
        _minX = minX;
        _maxY = maxY;
        _offX = offX;
        _offY = offY;
    }

    /// <summary>겉면들(의 상자)이 width × height 안(둘레 pad)에 들게 맞춘다. 겉면이 없으면 null.</summary>
    public static PlanMap? Fit(IEnumerable<MeshData> meshes, double width, double height, double pad)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var m in meshes)
        {
            if (m.Box.IsEmpty) continue;
            x0 = Math.Min(x0, m.Box.Min.X); y0 = Math.Min(y0, m.Box.Min.Y);
            x1 = Math.Max(x1, m.Box.Max.X); y1 = Math.Max(y1, m.Box.Max.Y);
        }
        if (x0 > x1) return null;
        double dx = Math.Max(x1 - x0, 0.1), dy = Math.Max(y1 - y0, 0.1);
        double scale = Math.Min((width - 2 * pad) / dx, (height - 2 * pad) / dy);
        if (!(scale > 0)) return null;
        // 가운데에 둔다.
        double offX = (width - dx * scale) / 2, offY = (height - dy * scale) / 2;
        return new PlanMap(width, height, scale, x0, y1, offX, offY);
    }

    /// <summary>장면의 점이 지도의 어디인가(픽셀 · 왼쪽 위 0,0 · 아래로 y 가 커진다).</summary>
    public (double x, double y) Map(Vec3 p) => (_offX + (p.X - _minX) * Scale, _offY + (_maxY - p.Y) * Scale);

    /// <summary>
    /// 겉면을 평면으로 누른 세모들(지도 픽셀 — 세모마다 x0 y0 x1 y1 x2 y2): 위나 아래를 보는 세모만(기울기 |nz| ≥ 0.2) · 지도에서 반 픽셀보다 작은 세모는 뺀다 ·
    /// 모두 같은 쪽으로 돌게 맞춘다(겹쳐 칠해도 지워지지 않게).
    /// </summary>
    public List<double> Footprint(MeshData mesh)
    {
        var flat = new List<double>();
        for (int i = 0; i + 2 < mesh.T.Length; i += 3)
        {
            Vec3 a = mesh.Vertex(mesh.T[i]), b = mesh.Vertex(mesh.T[i + 1]), c = mesh.Vertex(mesh.T[i + 2]);
            var n = (b - a).Cross(c - a);
            double len = n.Length;
            if (!(len > 1e-12) || Math.Abs(n.Z) < 0.2 * len) continue;
            if (Math.Abs(n.Z) / 2 * Scale * Scale < 0.5) continue;
            var (ax, ay) = Map(a);
            var (bx, by) = Map(b);
            var (cx, cy) = Map(c);
            // 도는 방향을 하나로(지도에서 시계 방향) — 윗면 · 밑면처럼 같은 자리에 반대로 도는 세모가 겹치면 «0 아님» 칠하기가 서로 지워 빈 그림이 된다.
            if ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax) < 0) (bx, by, cx, cy) = (cx, cy, bx, by);
            flat.Add(ax); flat.Add(ay); flat.Add(bx); flat.Add(by); flat.Add(cx); flat.Add(cy);
        }
        return flat;
    }
}
