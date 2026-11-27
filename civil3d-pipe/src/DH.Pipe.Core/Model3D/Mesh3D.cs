using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;

namespace DH.Pipe.Core.Model3D;

/// <summary>세모 · 네모 면으로 된 덩어리 하나.</summary>
public sealed class MeshData
{
    /// <summary>TRENCH · PIPE · CASING · SHAFT · SHORE</summary>
    public string Key { get; set; } = "";
    public List<P3> V { get; } = new();
    /// <summary>면(꼭짓점 번호 3 또는 4개).</summary>
    public List<int[]> F { get; } = new();

    /// <summary>고리 두 개(꼭짓점 수 같음) 사이를 네모로 잇는다. closed = 고리가 닫힘(관).</summary>
    public void Bridge(int a0, int b0, int n, bool closed)
    {
        int m = closed ? n : n - 1;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % n;
            F.Add(new[] { a0 + i, a0 + j, b0 + j, b0 + i });
        }
    }
}

/// <summary>
/// 3D 모형: 터파기 면 · 관 · 케이싱 · 수직구 · 흙막이 벽.
/// 측점마다 단면 테두리를 3D 로 놓고 이웃 측점끼리 잇는다. 꺾인 점의 단면은 반각 방향 한 장이라
/// 앞뒤 구간이 같은 테두리를 쓴다 → 코리더의 보타이(겹침)가 생길 수 없다.
/// </summary>
public static class Mesh3D
{
    private const int WallPts = 6;      // 벽 한쪽을 이만큼의 점으로 다시 나눔(측점마다 점 수를 같게)

    /// <summary>단면의 터파기 테두리를 정해진 점 수의 고리로(왼 벽 위 → 아래 · 오른 벽 아래 → 위).</summary>
    public static List<P2>? Ring(SectionResult r)
    {
        var line = r.Lines.FirstOrDefault(l => l.Key == "TRENCH");
        if (!r.Ok || line == null || line.Pts.Count < 3) return null;
        // 바닥 = 가장 낮은 높이의 점들 — 그 왼쪽이 왼 벽 · 오른쪽이 오른 벽
        double zb = line.Pts.Min(p => p.Y);
        int iL = line.Pts.FindIndex(p => p.Y <= zb + 1e-6), iR = line.Pts.FindLastIndex(p => p.Y <= zb + 1e-6);
        if (iL < 0 || iR <= iL) return null;
        var left = Resample(line.Pts.GetRange(0, iL + 1), WallPts);
        var right = Resample(line.Pts.GetRange(iR, line.Pts.Count - iR), WallPts);
        var ring = new List<P2>(left);
        ring.AddRange(right);
        return ring;
    }

    private static List<P2> Resample(List<P2> pts, int n)
    {
        var res = new List<P2>(n);
        if (pts.Count == 1) { for (int i = 0; i < n; i++) res.Add(pts[0]); return res; }
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
        double total = cum[^1];
        for (int k = 0; k < n; k++)
        {
            double t = total * k / (n - 1);
            int i = 0;
            while (i + 2 < pts.Count && cum[i + 1] < t) i++;
            double d = cum[i + 1] - cum[i], u = d < 1e-12 ? 0 : Math.Clamp((t - cum[i]) / d, 0, 1);
            res.Add(new P2(pts[i].X + (pts[i + 1].X - pts[i].X) * u, pts[i].Y + (pts[i + 1].Y - pts[i].Y) * u));
        }
        return res;
    }

    private static P3 At(PlanLine plan, double sta, double x, double z)
    {
        var c = plan.PointAt(sta);
        var r = plan.RightAt(sta);
        return new P3(c.X + r.X * x, c.Y + r.Y * x, z);
    }

    public static List<MeshData> Build(RouteContext c, IReadOnlyList<StationRow> rows, Summary sum, Pl ground)
    {
        var res = new List<MeshData>();
        var route = c.Route; var std = route.Std;

        // ---------------------------------------------------------------- 터파기 · 흙막이: 이웃 측점의 고리를 이음
        MeshData? trench = null, shore = null;
        int prevStart = -1;
        (int L, int R)? prevShore = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // 이 측점에서 끝나는 토막(전 단면) → 시작하는 토막(후 단면)
            foreach (bool after in new[] { false, true })
            {
                if (!after && i == 0) continue;
                if (after && i == rows.Count - 1) continue;
                var sec = after ? row.After : row.Before;
                var ring = Ring(sec);
                if (ring == null) { if (after) { prevStart = -1; prevShore = null; } continue; }
                trench ??= New(res, "TRENCH");
                int start = trench.V.Count;
                foreach (var p in ring) trench.V.Add(At(c.Plan, row.Sta, p.X, p.Y));
                if (!after && prevStart >= 0) trench.Bridge(prevStart, start, ring.Count, false);
                if (after) prevStart = start;

                (int, int)? sh = null;
                var sl = sec.Lines.Where(l => l.Key == "SHORE").ToList();
                if (sl.Count == 2)
                {
                    shore ??= New(res, "SHORE");
                    int s0 = shore.V.Count;
                    foreach (var l in sl) foreach (var p in l.Pts) shore.V.Add(At(c.Plan, row.Sta, p.X, p.Y));
                    sh = (s0, s0 + 2);
                    if (!after && prevShore.HasValue)
                    {
                        shore.F.Add(new[] { prevShore.Value.L, prevShore.Value.L + 1, s0 + 1, s0 });
                        shore.F.Add(new[] { prevShore.Value.R, prevShore.Value.R + 1, s0 + 3, s0 + 2 });
                    }
                }
                if (after) prevShore = sh;
            }
        }

        // ---------------------------------------------------------------- 관 · 케이싱(12각 고리)
        if (!c.Profile.IsEmpty && rows.Count >= 2)
        {
            var stas = new SortedSet<double>();
            foreach (var row in rows) stas.Add(row.Sta);
            foreach (var p in c.Profile.Pvi) if (p.S >= rows[0].Sta && p.S <= rows[^1].Sta) stas.Add(p.S);
            var list = stas.ToList();
            var pipe = New(res, "PIPE");
            int prev = -1;
            PipeSpec? prevSpec = null;
            foreach (double s in list)
            {
                var spec = route.PipeAt(s);
                if (prevSpec != null && Math.Abs(prevSpec.Od - spec.Od) > 1e-9)
                {
                    // 관경이 바뀜 — 같은 자리에 앞 관경 고리를 한 번 더 놓고 끊는다
                    int e0 = pipe.V.Count;
                    AddTube(pipe, c.Plan, s, PipeGeom.OuterBottom(prevSpec, c.Profile.At(s)) + prevSpec.Od * 0.5, prevSpec.Od * 0.5);
                    if (prev >= 0) pipe.Bridge(prev, e0, 12, true);
                    prev = -1;
                }
                int st = pipe.V.Count;
                AddTube(pipe, c.Plan, s, PipeGeom.OuterBottom(spec, c.Profile.At(s)) + spec.Od * 0.5, spec.Od * 0.5);
                if (prev >= 0) pipe.Bridge(prev, st, 12, true);
                prev = st;
                prevSpec = spec;
            }
        }
        foreach (var sq in sum.Spans)
        {
            if (sq.Span.Kind != MethodKind.Jacking) continue;
            var spec = route.PipeAt(0.5 * (sq.StaFrom + sq.StaTo));
            var cas = New(res, "CASING");
            int prev = -1;
            foreach (double s in new[] { sq.StaFrom, sq.StaTo })
            {
                int st = cas.V.Count;
                AddTube(cas, c.Plan, s, PipeGeom.OuterBottom(spec, c.Profile.At(s)) + spec.Od * 0.5, sq.CasingOd * 0.5);
                if (prev >= 0) cas.Bridge(prev, st, 12, true);
                prev = st;
            }
            // 수직구: 상자 또는 24각 통(바닥 · 옆면)
            foreach (var sh in sq.Shafts)
            {
                var m = New(res, "SHAFT");
                var center = c.Plan.PointAt(sh.Sta);
                var right = c.Plan.RightAt(sh.Sta);
                var fwd = new P2(-right.Y, right.X);
                var outline = new List<P2>();
                if (sh.Shape == ShaftShape.Rect)
                {
                    foreach (var (u, v) in new[] { (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5) })
                        outline.Add(new P2(center.X + fwd.X * u * sh.L + right.X * v * sh.W, center.Y + fwd.Y * u * sh.L + right.Y * v * sh.W));
                }
                else
                {
                    for (int k = 0; k < 24; k++)
                    {
                        double t = k * Math.PI / 12;
                        outline.Add(new P2(center.X + Math.Cos(t) * sh.W * 0.5, center.Y + Math.Sin(t) * sh.W * 0.5));
                    }
                }
                int n = outline.Count;
                foreach (var p in outline) m.V.Add(new P3(p.X, p.Y, sh.GroundZ));
                foreach (var p in outline) m.V.Add(new P3(p.X, p.Y, sh.BottomZ));
                m.Bridge(0, n, n, true);
                if (n == 4) m.F.Add(new[] { n, n + 1, n + 2, n + 3 });
                else for (int k = 1; k + 1 < n; k++) m.F.Add(new[] { n, n + k, n + k + 1 });
            }
        }
        res.RemoveAll(m => m.F.Count == 0);
        return res;
    }

    private static MeshData New(List<MeshData> list, string key)
    {
        var m = new MeshData { Key = key };
        list.Add(m);
        return m;
    }

    private static void AddTube(MeshData m, PlanLine plan, double sta, double centerZ, double r)
    {
        for (int k = 0; k < 12; k++)
        {
            double t = k * Math.PI / 6;
            m.V.Add(At(plan, sta, Math.Cos(t) * r, centerZ + Math.Sin(t) * r));
        }
    }
}
