namespace DH.Grading.Core;

/// <summary>★★★[v107 · 계획_v107.md 1.2 · JACK 1027 «인프라웍스 내보내기를 3D 웹뷰로»] <b>옹벽 세모 메시</b> — [3D 보기]가 그리는 옹벽(몸통 · 모서리 메움 · 무늬 조각).
/// <para>옹벽3D.dwg와 <b>같은 점</b>: 덩어리 링 점은 <see cref="LoftRings"/> 하나를 DWG(<c>WallSolidDwg.Loft</c>)와 함께 쓴다 · 모서리 메움은 DWG의 회전(ACIS)을 5° 이하 조각으로 · 무늬 조각은 같은 판 틀(u, v, w)의 프리즘.</para>
/// <para>면은 다각형 원래 차례대로 세모로 나누고(오목한 단면 · 반쪽 단면 · 조각은 귀 자르기) · 같은 점은 합치고(1 µm 칸) · 넓이 0 세모는 버린 뒤 · 부피 부호로 바깥을 맞춘다(DWG Emit과 같은 법).</para></summary>
public static class WallMesh
{
    /// <summary>모서리 메움 회전 한 조각의 최대 각(도).</summary>
    public const double CornerStepDeg = 5.0;

    /// <summary>세모 메시 — 점(월드) · 세모(점 번호 셋씩) · 귀 자르기를 못 해 부채꼴로 넘긴 면 수.</summary>
    public sealed class Mesh
    {
        public readonly List<Point3> V = new();
        public readonly List<int> T = new();
        public int FanFallback;
        readonly Dictionary<(long, long, long), int> _weld = new();

        public int Add(Point3 p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
            if (_weld.TryGetValue(key, out int i)) return i;
            V.Add(p); _weld[key] = V.Count - 1;
            return V.Count - 1;
        }

        void Tri(int a, int b, int c)
        {
            if (a == b || b == c || c == a) return;
            var p = V[a]; var q = V[b]; var r = V[c];
            double ux = q.X - p.X, uy = q.Y - p.Y, uz = q.Z - p.Z, vx = r.X - p.X, vy = r.Y - p.Y, vz = r.Z - p.Z;
            double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * cx + cy * cy + cz * cz < 1e-20) return;                      // 넓이 0(1e-10 ㎡ 아래)
            T.Add(a); T.Add(b); T.Add(c);
        }

        /// <summary>네 점 면(옆면) — 차례대로 세모 둘.</summary>
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>다각형 면 — <paramref name="idx"/> 차례(감김)를 지키며 2D 좌표 <paramref name="uv"/>로 귀 자르기.</summary>
        public void Poly(IReadOnlyList<int> idx, IReadOnlyList<(double X, double Y)> uv)
        {
            int n = idx.Count;
            if (n < 3) return;
            if (n == 3) { Tri(idx[0], idx[1], idx[2]); return; }
            double area = 0;
            for (int i = 0; i < n; i++) { var a = uv[i]; var b = uv[(i + 1) % n]; area += a.X * b.Y - b.X * a.Y; }
            bool ccw = area > 0;
            // 2D에서 반시계로 돌리며 귀를 찾는다 — 낸 세모는 원래 감김으로 되돌려 넣는다
            var L = new List<int>(n);
            for (int i = 0; i < n; i++) L.Add(ccw ? i : n - 1 - i);
            void Emit(int i, int j, int k) { if (ccw) Tri(idx[i], idx[j], idx[k]); else Tri(idx[i], idx[k], idx[j]); }
            double scale = 0;
            for (int i = 0; i < n; i++) scale = Math.Max(scale, Math.Max(Math.Abs(uv[i].X - uv[0].X), Math.Abs(uv[i].Y - uv[0].Y)));
            double eps = 1e-12 * Math.Max(1, scale * scale);
            int guard = 0;
            while (L.Count > 3 && guard++ < 10 * n)
            {
                bool cut = false;
                for (int t = 0; t < L.Count; t++)
                {
                    int ia = L[(t + L.Count - 1) % L.Count], ib = L[t], ic = L[(t + 1) % L.Count];
                    var a = uv[ia]; var b = uv[ib]; var c = uv[ic];
                    double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                    if (cr <= eps) continue;                                           // 오목 · 일직선
                    bool blocked = false;
                    foreach (int ip in L)
                    {
                        if (ip == ia || ip == ib || ip == ic) continue;
                        var p = uv[ip];
                        if (p.X == a.X && p.Y == a.Y || p.X == c.X && p.Y == c.Y) continue;   // 같은 자리 점(닫힌 링 겹침)
                        if (InTri(p, a, b, c, eps)) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    Emit(ia, ib, ic);
                    L.RemoveAt(t);
                    cut = true;
                    break;
                }
                if (!cut)
                {
                    // 귀가 없다(일직선 점 · 꼬인 면) — 일직선 점을 하나 빼거나, 그것도 없으면 남은 것을 부채꼴로
                    int drop = -1; double best = double.MaxValue;
                    for (int t = 0; t < L.Count; t++)
                    {
                        var a = uv[L[(t + L.Count - 1) % L.Count]]; var b = uv[L[t]]; var c = uv[L[(t + 1) % L.Count]];
                        double cr = Math.Abs((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X));
                        if (cr < best) { best = cr; drop = t; }
                    }
                    if (drop >= 0 && best <= eps * 1e3) { L.RemoveAt(drop); continue; }
                    FanFallback++;
                    for (int t = 1; t + 1 < L.Count; t++) Emit(L[0], L[t], L[t + 1]);
                    return;
                }
            }
            if (L.Count == 3) Emit(L[0], L[1], L[2]);
        }

        static bool InTri((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c, double eps)
        {
            double d1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double d2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X);
            double d3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);
            return d1 >= -eps && d2 >= -eps && d3 >= -eps;                            // 안 또는 변 위(새 대각선 위 점도 막는다)
        }

        /// <summary>부피(발산 정리) — 닫힌 메시면 부호로 안팎을 안다.</summary>
        public double Volume()
        {
            double v = 0;
            for (int i = 0; i + 2 < T.Count; i += 3)
            {
                var a = V[T[i]]; var b = V[T[i + 1]]; var c = V[T[i + 2]];
                v += a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
            }
            return v / 6;
        }

        /// <summary>바깥이 겉이 되게 — 부피가 음수면 모든 세모를 뒤집는다.</summary>
        public void Orient()
        {
            if (Volume() >= 0) return;
            for (int i = 0; i + 2 < T.Count; i += 3) (T[i + 1], T[i + 2]) = (T[i + 2], T[i + 1]);
        }

        /// <summary>닫힘 검사 — 방향 있는 변이 한 번씩이고 반대 변이 꼭 있으면 0(물 샐 틈 없음). 아니면 어긋난 변 수.</summary>
        public int OpenEdges()
        {
            var e = new Dictionary<(int, int), int>();
            for (int i = 0; i + 2 < T.Count; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var key = (T[i + k], T[i + (k + 1) % 3]);
                    e[key] = e.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            int bad = 0;
            foreach (var kv in e)
            {
                if (kv.Value != 1) { bad++; continue; }
                if (!e.TryGetValue((kv.Key.Item2, kv.Key.Item1), out int r) || r != 1) bad++;
            }
            return bad;
        }
    }

    /// <summary>덩어리 링 점 — 조각마다 (재료 · 링 꼭짓점 수 · 정거장 차례 점들). 정거장마다 링 꼴이 다르면 그 조각은 <c>null</c>(DWG와 같은 실패).
    /// <c>WallSolidDwg.Loft</c>도 이 점을 그대로 쓴다(3D 보기 = 옹벽3D.dwg).</summary>
    public static List<(string Mat, int M, List<Point3>? V)> LoftRings(List<WallSweep.Sta> run, bool lift)
    {
        var res = new List<(string, int, List<Point3>?)>();
        var parts0 = WallSweep.Solids(run[0].Shape);
        for (int pi = 0; pi < parts0.Count; pi++)
        {
            string mat = parts0[pi].Material;
            int m = parts0[pi].Ring.Count;
            var verts = new List<Point3>(m * run.Count);
            bool ok = true;
            foreach (var s in run)
            {
                var ps = WallSweep.Solids(s.Shape);
                if (pi >= ps.Count || ps[pi].Ring.Count != m) { ok = false; break; }
                foreach (var q in ps[pi].Ring) verts.Add(WallSweep.World(s, q.X, q.Z, lift));
            }
            res.Add((mat, m, ok ? verts : null));
        }
        return res;
    }

    /// <summary>덩어리 — 조각마다 닫힌 메시(옆면 = 정거장 사이 네 점 면 · 양끝 마구리 = 그 정거장 단면 귀 자르기). 링 꼴이 다른 조각은 <c>null</c>.</summary>
    public static List<(string Mat, Mesh? M)> Loft(List<WallSweep.Sta> run, bool lift = true)
    {
        var res = new List<(string, Mesh?)>();
        var rings = LoftRings(run, lift);
        for (int pi = 0; pi < rings.Count; pi++)
        {
            var (mat, m, V) = rings[pi];
            if (V == null) { res.Add((mat, null)); continue; }
            var mesh = new Mesh();
            int ns = run.Count;
            var id = new int[ns * m];
            for (int i = 0; i < V.Count; i++) id[i] = mesh.Add(V[i]);
            for (int k = 0; k + 1 < ns; k++)
                for (int j = 0; j < m; j++)
                    mesh.Quad(id[k * m + j], id[k * m + (j + 1) % m], id[(k + 1) * m + (j + 1) % m], id[(k + 1) * m + j]);
            // 마구리 — DWG와 같은 감김(시작 = 거꾸로 · 끝 = 차례대로) · 2D는 그 정거장 단면 링
            var r0 = WallSweep.Solids(run[0].Shape)[pi].Ring;
            var r1 = WallSweep.Solids(run[ns - 1].Shape)[pi].Ring;
            mesh.Poly(Enumerable.Range(0, m).Reverse().Select(j => id[j]).ToList(), Enumerable.Range(0, m).Reverse().Select(j => r0[j]).ToList());
            mesh.Poly(Enumerable.Range(0, m).Select(j => id[(ns - 1) * m + j]).ToList(), r1);
            mesh.Orient();
            res.Add((mat, mesh));
        }
        return res;
    }

    /// <summary>모서리 메움 — DWG <c>Corner</c>와 같은 반쪽 단면(전단 · 꼭대기 올림 뒤 x' = 0에서 자름)을 세로축으로 ang만큼 돌린다(<see cref="CornerStepDeg"/> 이하 조각).</summary>
    public static List<(string Mat, Mesh M)> Corner(WallSweep.Corner c, bool lift = true)
    {
        var res = new List<(string, Mesh)>();
        var p = c.P;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(c.Ang) * 180 / Math.PI / CornerStepDeg));
        foreach (var part in WallSweep.Solids(p.Shape))
        {
            var sta = new WallSweep.Sta(0, 0, 0, 1, 0, p.Shape, 1.0, c.Lean, -1);
            var ring = part.Ring.Select(q => { var w = WallSweep.World(sta, q.X, q.Z, lift); return (X: w.X, Z: w.Z); }).ToList();
            foreach (bool soil in new[] { true, false })
            {
                var half = ClipHalf(ring, soil);
                if (half.Count < 3 || Math.Abs(WallSection.Area(half)) < 1e-6) continue;
                var mesh = new Mesh();
                int m = half.Count;
                var id = new int[(steps + 1) * m];
                for (int k = 0; k <= steps; k++)
                {
                    double th = c.Ang * k / steps, cs = Math.Cos(th), sn = Math.Sin(th);
                    double dx = c.N1x * cs - c.N1y * sn, dy = c.N1x * sn + c.N1y * cs;   // n1을 +Z 둘레로 th만큼(DWG: ang ≥ 0 이면 +Z 반시계)
                    for (int j = 0; j < m; j++)
                        id[k * m + j] = mesh.Add(new Point3(p.X + dx * half[j].X, p.Y + dy * half[j].X, p.Toe + half[j].Z));
                }
                for (int k = 0; k < steps; k++)
                    for (int j = 0; j < m; j++)
                        mesh.Quad(id[k * m + j], id[k * m + (j + 1) % m], id[(k + 1) * m + (j + 1) % m], id[(k + 1) * m + j]);
                var uv = half.Select(q => (q.X, q.Z)).ToList();
                mesh.Poly(Enumerable.Range(0, m).Reverse().Select(j => id[j]).ToList(), Enumerable.Range(0, m).Reverse().Select(j => uv[j]).ToList());
                mesh.Poly(Enumerable.Range(0, m).Select(j => id[steps * m + j]).ToList(), uv);
                if (mesh.T.Count == 0) continue;
                mesh.Orient();
                res.Add((part.Material, mesh));
            }
        }
        return res;
    }

    /// <summary>링(전단 뒤 x')을 x' ≥ 0(흙 쪽) 또는 x' ≤ 0(앞쪽) 반으로 자른다 — DWG <c>ClipHalf</c>와 같은 식.</summary>
    public static List<(double X, double Z)> ClipHalf(IReadOnlyList<(double X, double Z)> r, bool soil)
    {
        const double cut = 0;
        var o = new List<(double X, double Z)>();
        bool In((double X, double Z) q) => soil ? q.X >= cut - 1e-12 : q.X <= cut + 1e-12;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            bool ia = In(a), ib = In(b);
            if (ia) o.Add(a);
            if (ia != ib)
            {
                double t = (cut - a.X) / (b.X - a.X);
                o.Add((cut, a.Z + (b.Z - a.Z) * t));
            }
        }
        var res = new List<(double X, double Z)>();
        foreach (var q in o) if (res.Count == 0 || Math.Abs(res[^1].X - q.X) > 1e-9 || Math.Abs(res[^1].Z - q.Z) > 1e-9) res.Add(q);
        while (res.Count > 1 && Math.Abs(res[0].X - res[^1].X) <= 1e-9 && Math.Abs(res[0].Z - res[^1].Z) <= 1e-9) res.RemoveAt(res.Count - 1);
        return res;
    }

    /// <summary>무늬 조각 — 판 틀(O + u·U + v·V + w·W)에서 w ∈ [−묻힘, +내밀기] 프리즘(DWG <c>Piece</c>와 같은 몸: 직사각형 상자 · 그 밖 돌출).</summary>
    public static Mesh Piece(WallFacePattern.Sheet sh, WallFacePattern.Piece pc)
    {
        var mesh = new Mesh();
        var poly = pc.Poly;
        if (pc.Rect)
        {
            double u0 = poly.Min(q => q.U), u1 = poly.Max(q => q.U), v0 = poly.Min(q => q.V), v1 = poly.Max(q => q.V);
            poly = new List<(double U, double V)> { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };
        }
        int n = poly.Count;
        if (n < 3) return mesh;
        Point3 At(double u, double v, double w) => new(
            sh.O.X + u * sh.U.X + v * sh.V.X + w * sh.W.X,
            sh.O.Y + u * sh.U.Y + v * sh.V.Y + w * sh.W.Y,
            sh.O.Z + u * sh.U.Z + v * sh.V.Z + w * sh.W.Z);
        var bot = new int[n]; var top = new int[n];
        for (int i = 0; i < n; i++) { bot[i] = mesh.Add(At(poly[i].U, poly[i].V, -pc.Embed)); top[i] = mesh.Add(At(poly[i].U, poly[i].V, pc.Relief)); }
        var uv = poly.Select(q => (q.U, q.V)).ToList();
        mesh.Poly(top, uv);                                                               // 겉
        mesh.Poly(bot.Reverse().ToList(), uv.AsEnumerable().Reverse().ToList());        // 묻힌 밑
        for (int i = 0; i < n; i++) mesh.Quad(bot[i], bot[(i + 1) % n], top[(i + 1) % n], top[i]);
        mesh.Orient();
        return mesh;
    }
}
