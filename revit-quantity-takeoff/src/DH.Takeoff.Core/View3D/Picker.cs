namespace DH.Takeoff.Core.View3D;

/// <summary>
/// 3D 팝업에서 부재를 찍는 계산 — 화면의 한 점을 지나는 시선이 맨 먼저 닿는 부재, 화면에 끈 네모에 드는 부재.
/// 그림을 그리는 쪽(WPF)의 찍기 기능을 쓰지 않고 직접 센다: 화면 없이도 같은 답이 나오고 시험할 수 있다.
/// </summary>
public static class Picker
{
    /// <summary>시선이 맨 먼저 닿는 겉면. index = meshes 의 차례(없으면 −1), distance = 시선의 출발점에서 닿은 곳까지(보는 방향으로, 음수일 수 있다).</summary>
    public static (int index, double distance) Pick(IReadOnlyList<MeshData> meshes, Func<int, bool> shown, Vec3 origin, Vec3 direction)
    {
        var dir = direction.Unit();
        int best = -1;
        double bestT = double.MaxValue;
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            if (mesh.T.Length == 0 || !shown(m) || !HitsBox(mesh.Box, origin, dir)) continue;
            for (int i = 0; i + 2 < mesh.T.Length; i += 3)
            {
                double t = HitTriangle(origin, dir, mesh.Vertex(mesh.T[i]), mesh.Vertex(mesh.T[i + 1]), mesh.Vertex(mesh.T[i + 2]));
                if (t < bestT) { bestT = t; best = m; }
            }
        }
        return best >= 0 ? (best, bestT) : (-1, 0);
    }

    /// <summary>
    /// 시선이 닿는 겉면 전부 — 가까운 것부터(거리가 같으면 차례순). 겹쳐 그려져 맨 앞 것만 보이는 것들을 차례로 고를 때 쓴다.
    /// 겉면마다 한 번만 나온다(그 겉면에서 가장 가까이 닿은 자리의 거리).
    /// </summary>
    public static List<(int index, double distance)> PickAll(IReadOnlyList<MeshData> meshes, Func<int, bool> shown, Vec3 origin, Vec3 direction)
    {
        var dir = direction.Unit();
        var hits = new List<(int index, double distance)>();
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            if (mesh.T.Length == 0 || !shown(m) || !HitsBox(mesh.Box, origin, dir)) continue;
            double nearest = double.MaxValue;
            for (int i = 0; i + 2 < mesh.T.Length; i += 3)
            {
                double t = HitTriangle(origin, dir, mesh.Vertex(mesh.T[i]), mesh.Vertex(mesh.T[i + 1]), mesh.Vertex(mesh.T[i + 2]));
                if (t < nearest) nearest = t;
            }
            if (nearest < double.MaxValue) hits.Add((m, nearest));
        }
        hits.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.index.CompareTo(b.index));
        return hits;
    }

    // 시선(양쪽으로 끝없는 직선 — 직교 그림에서는 화면 평면 앞뒤의 것이 다 보인다)이 상자를 지나는가.
    private static bool HitsBox(Box3 box, Vec3 o, Vec3 d)
    {
        if (box.IsEmpty) return false;
        double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
        const double pad = 1e-6;
        if (!Slab(o.X, d.X, box.Min.X - pad, box.Max.X + pad, ref t0, ref t1)) return false;
        if (!Slab(o.Y, d.Y, box.Min.Y - pad, box.Max.Y + pad, ref t0, ref t1)) return false;
        return Slab(o.Z, d.Z, box.Min.Z - pad, box.Max.Z + pad, ref t0, ref t1);
    }

    private static bool Slab(double o, double d, double lo, double hi, ref double t0, ref double t1)
    {
        if (Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
        double a = (lo - o) / d, b = (hi - o) / d;
        if (a > b) (a, b) = (b, a);
        t0 = Math.Max(t0, a);
        t1 = Math.Min(t1, b);
        return t0 <= t1;
    }

    // 직선과 세모가 만나는 자리(Möller–Trumbore, 양면). 만나지 않으면 +∞.
    private static double HitTriangle(Vec3 o, Vec3 d, Vec3 a, Vec3 b, Vec3 c)
    {
        Vec3 e1 = b - a, e2 = c - a;
        var h = d.Cross(e2);
        double det = e1.Dot(h);
        if (Math.Abs(det) < 1e-14) return double.PositiveInfinity;   // 시선이 세모와 나란하다
        double inv = 1 / det;
        var s = o - a;
        double u = s.Dot(h) * inv;
        if (u < -1e-9 || u > 1 + 1e-9) return double.PositiveInfinity;
        var q = s.Cross(e1);
        double v = d.Dot(q) * inv;
        if (v < -1e-9 || u + v > 1 + 1e-9) return double.PositiveInfinity;
        return e2.Dot(q) * inv;
    }

    /// <summary>
    /// 화면에 끈 네모(픽셀)에 드는 겉면들. crossing = false 면 화면에 비친 모습이 네모 안에 온전히 든 것만,
    /// true 면 네모에 조금이라도 걸친 것까지(Revit 의 왼→오 · 오→왼 끌기와 같다). 가려진 것도 고른다.
    /// </summary>
    public static List<int> InRect(IReadOnlyList<MeshData> meshes, Func<int, bool> shown, OrbitCamera camera,
                                   double x0, double y0, double x1, double y1, bool crossing)
    {
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (y0 > y1) (y0, y1) = (y1, y0);
        var picked = new List<int>();
        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            if (mesh.P.Length == 0 || !shown(m)) continue;
            // 상자의 비친 범위로 먼저 가른다.
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            foreach (var c in mesh.Box.Corners())
            {
                var s = camera.Project(c);
                bx0 = Math.Min(bx0, s.x); by0 = Math.Min(by0, s.y); bx1 = Math.Max(bx1, s.x); by1 = Math.Max(by1, s.y);
            }
            if (bx1 < x0 || bx0 > x1 || by1 < y0 || by0 > y1) continue;                              // 네모 밖
            if (bx0 >= x0 && bx1 <= x1 && by0 >= y0 && by1 <= y1) { picked.Add(m); continue; }       // 상자째 네모 안

            int count = mesh.P.Length / 3;
            var sx = new double[count];
            var sy = new double[count];
            bool allInside = true, anyInside = false;
            for (int i = 0; i < count; i++)
            {
                var s = camera.Project(mesh.Vertex(i));
                sx[i] = s.x; sy[i] = s.y;
                bool inside = s.x >= x0 && s.x <= x1 && s.y >= y0 && s.y <= y1;
                allInside &= inside;
                anyInside |= inside;
            }
            if (!crossing) { if (allInside) picked.Add(m); continue; }
            if (anyInside) { picked.Add(m); continue; }
            // 꼭짓점은 하나도 안 들었지만 비친 세모가 네모를 덮거나 가로지를 수 있다(큰 벽 한가운데에 끈 네모).
            for (int i = 0; i + 2 < mesh.T.Length; i += 3)
            {
                int a = mesh.T[i], b = mesh.T[i + 1], c = mesh.T[i + 2];
                if (TriangleMeetsRect(sx[a], sy[a], sx[b], sy[b], sx[c], sy[c], x0, y0, x1, y1)) { picked.Add(m); break; }
            }
        }
        return picked;
    }

    private static bool TriangleMeetsRect(double ax, double ay, double bx, double by, double cx, double cy, double x0, double y0, double x1, double y1)
    {
        // 네모의 귀가 세모 안에 있거나, 세모의 변이 네모의 변을 가로지르면 겹친다(세모의 꼭짓점이 네모 안인 경우는 부른 쪽이 이미 봤다).
        if (InTriangle(x0, y0, ax, ay, bx, by, cx, cy) || InTriangle(x1, y0, ax, ay, bx, by, cx, cy) ||
            InTriangle(x1, y1, ax, ay, bx, by, cx, cy) || InTriangle(x0, y1, ax, ay, bx, by, cx, cy)) return true;
        return SegmentMeetsRect(ax, ay, bx, by, x0, y0, x1, y1) || SegmentMeetsRect(bx, by, cx, cy, x0, y0, x1, y1) || SegmentMeetsRect(cx, cy, ax, ay, x0, y0, x1, y1);
    }

    private static bool InTriangle(double px, double py, double ax, double ay, double bx, double by, double cx, double cy)
    {
        double d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
        double d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
        double d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    // 선분을 네모로 자른다(Liang–Barsky): 남는 것이 있으면 만난다.
    private static bool SegmentMeetsRect(double ax, double ay, double bx, double by, double x0, double y0, double x1, double y1)
    {
        double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
        return Clip(-dx, ax - x0, ref t0, ref t1) && Clip(dx, x1 - ax, ref t0, ref t1) &&
               Clip(-dy, ay - y0, ref t0, ref t1) && Clip(dy, y1 - ay, ref t0, ref t1);
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        double r = q / p;
        if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }
}
