namespace DH.Grading.Core;

// ★★★[v115 · JACK 1105 Revit 구조물 · 계획 2026-11-05_Revit구조물_투영 4절] 구조물 형상 — 순수(Civil 없음) · 하네스 S220~S234가 잰다.
//   Civil은 개체(폴리페이스 메시 · 폴리곤 메시 · 3D 면 · 서브D 메시 · 솔리드 메시)를 «국소 좌표 m» 삼각형으로 바꿔 StructSoup에 넣기만 한다.
//   여기서: 0.1 mm 용접 → 퇴화 버림 → 같은 레이어 안 연결 덩어리 = 부재 → 모서리 검사(열린 · 비다양체 · 감김 어긋남) → 닫힌 부재만 부호 부피로 뒤집기 → XY 격자 색인.

/// <summary>★[v115 · JACK 1105 Revit 구조물] 삼각형 모음 — 국소 좌표(m, 원점 <see cref="Ox"/>·<see cref="Oy"/>·<see cref="Oz"/>)와 레이어.
/// 사각형 · 다각형 면은 부채꼴로 나눈다(메시 면은 볼록). 대칭 변환(det &lt; 0)을 거친 꼭짓점이면 <c>flip</c>으로 감김을 뒤집어 넣는다.</summary>
public sealed class StructSoup
{
    public double Ox, Oy, Oz;
    internal readonly List<double> P = new();
    internal readonly List<int> L = new();
    public readonly List<string> Layers = new();
    readonly Dictionary<string, int> _li = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>못 쓴 개체(종류 모름 · 메시로 못 바꿈) · 나눈 다각형 면 수(사각형 포함).</summary>
    public int Unused, Polys;
    public int TriCount => L.Count;

    /// <summary>국소 원점 — 모형 상자 최소를 1 m로 내림(계획 4절).</summary>
    public static (double X, double Y, double Z) OriginFor(double minX, double minY, double minZ)
        => (Math.Floor(minX), Math.Floor(minY), Math.Floor(minZ));

    int Lay(string? name)
    {
        name ??= "";
        if (_li.TryGetValue(name, out int i)) return i;
        i = Layers.Count; Layers.Add(name); _li[name] = i;
        return i;
    }

    public void Add(Point3 a, Point3 b, Point3 c, string? layer, bool flip = false)
    {
        if (flip) (b, c) = (c, b);
        P.Add(a.X); P.Add(a.Y); P.Add(a.Z); P.Add(b.X); P.Add(b.Y); P.Add(b.Z); P.Add(c.X); P.Add(c.Y); P.Add(c.Z);
        L.Add(Lay(layer));
    }

    /// <summary>다각형 면 하나(꼭짓점 색인 0부터) — 부채꼴 삼각형.</summary>
    public void AddPolygon(IReadOnlyList<Point3> v, IReadOnlyList<int> idx, string? layer, bool flip = false)
    {
        var q = new List<int>(idx.Count);
        foreach (int i in idx) if (i >= 0 && i < v.Count && (q.Count == 0 || (q[^1] != i && v[q[^1]] != v[i]))) q.Add(i);
        if (q.Count > 1 && (q[0] == q[^1] || v[q[0]] == v[q[^1]])) q.RemoveAt(q.Count - 1);
        if (q.Count < 3) return;
        if (q.Count > 3) Polys++;
        for (int k = 1; k + 1 < q.Count; k++) Add(v[q[0]], v[q[k]], v[q[k + 1]], layer, flip);
    }

    /// <summary>면 배열 [n, i0 … i(n−1), n, …] (SubDMesh · GetObjectMesh 꼴). 읽은 면 수.</summary>
    public int AddFaceList(IReadOnlyList<Point3> v, IReadOnlyList<int> faceArray, string? layer, bool flip = false)
    {
        int k = 0, n = 0;
        var idx = new List<int>();
        while (k < faceArray.Count)
        {
            int c = faceArray[k++];
            if (c <= 0 || k + c > faceArray.Count) break;
            idx.Clear();
            for (int j = 0; j < c; j++) idx.Add(faceArray[k + j]);
            k += c;
            AddPolygon(v, idx, layer, flip);
            n++;
        }
        return n;
    }

    /// <summary>폴리페이스 메시 면(색인 1부터 · 음수 = 숨은 모서리일 뿐 면은 그대로 · 0 = 안 씀).</summary>
    public void AddPolyFace(IReadOnlyList<Point3> v, IReadOnlyList<int[]> faces, string? layer, bool flip = false)
    {
        var idx = new List<int>(4);
        foreach (var f in faces)
        {
            idx.Clear();
            foreach (int r in f) { int a = Math.Abs(r); if (a > 0) idx.Add(a - 1); }
            AddPolygon(v, idx, layer, flip);
        }
    }

    /// <summary>폴리곤 메시 M×N(행 우선 · 색인 = i·N + j) — 닫힌 M · N이면 끝과 처음을 잇는다.</summary>
    public void AddGrid(IReadOnlyList<Point3> v, int m, int n, bool mClosed, bool nClosed, string? layer, bool flip = false)
    {
        if (m < 2 || n < 2 || v.Count < m * n) return;
        int mi = mClosed ? m : m - 1, nj = nClosed ? n : n - 1;
        for (int i = 0; i < mi; i++)
            for (int j = 0; j < nj; j++)
            {
                int i1 = (i + 1) % m, j1 = (j + 1) % n;
                AddPolygon(v, new[] { i * n + j, i * n + j1, i1 * n + j1, i1 * n + j }, layer, flip);
            }
    }
}

/// <summary>부재 하나 = 용접 뒤 <b>같은 레이어 안</b> 연결 덩어리(계획 4절 · 검토 M2).</summary>
public sealed class StructPart
{
    public int Layer, Tris;
    /// <summary>열린 모서리(쓰인 수가 홀수) · 비다양체(3 이상 짝수) · 감김 어긋남(둘인데 같은 방향).</summary>
    public int Open, NonManifold, Misoriented;
    public double Volume;
    /// <summary>닫힌 부재(열린 모서리 0)인데 부호 부피가 음수라 뒤집었다.</summary>
    public bool Flipped;
    /// <summary>해치를 칠 부재(검토 M2 — 빼는 이름이 아니면 다).</summary>
    public bool Hatch = true;
    /// <summary>지형 등 — 자르기 · 평면 외곽에서 뺀다(D6).</summary>
    public bool Excluded;
    public double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
    public bool Closed => Open == 0;
}

/// <summary>★[v115 · JACK 1105 Revit 구조물] 용접한 구조물 메시 + 부재 + XY 격자 색인(커튼 자르기가 선을 따라 묻는다).</summary>
public sealed class StructModel
{
    public const double WeldTol = 1e-4;   // 0.1 mm — Civil TIN이 합치는 거리와 같다
    public double Ox, Oy, Oz;
    public double[] V = Array.Empty<double>();
    public int[] T = Array.Empty<int>();
    public int[] TriPart = Array.Empty<int>();
    public List<StructPart> Parts = new();
    public List<string> Layers = new();
    public int InTris, Degenerate, Welded, Flipped, Unused;
    /// <summary>국소 상자(뺀 부재 제외).</summary>
    public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
    public bool Empty => TriCount == 0 || MinX > MaxX;
    public int TriCount => T.Length / 3;
    public int OpenParts => Parts.Count(p => !p.Excluded && p.Open > 0);
    public int NonManifoldParts => Parts.Count(p => !p.Excluded && p.NonManifold > 0);
    public int MisorientedParts => Parts.Count(p => !p.Excluded && p.Misoriented > 0);

    // XY 격자(국소) — 셀마다 삼각형 목록(CSR)
    internal double Gx0, Gy0, Gc = 1;
    internal int Gnx, Gny;
    internal int[] GStart = Array.Empty<int>(), GItems = Array.Empty<int>();
    int[] _stamp = Array.Empty<int>();
    int _stampN;

    /// <summary>용접 · 부재 · 검사 · 뒤집기 · 색인.</summary>
    public static StructModel Build(StructSoup s, double weld = WeldTol)
    {
        var m = new StructModel { Ox = s.Ox, Oy = s.Oy, Oz = s.Oz, Layers = s.Layers.ToList(), InTris = s.TriCount, Unused = s.Unused };
        int nt = s.TriCount;
        // ① 용접 — 셀 크기 weld 격자 · 같은 셀 먼저 · 없으면 이웃 26칸에서 weld 안 꼭짓점
        var map = new Dictionary<(long, long, long), int>(nt * 2);
        var vx = new List<double>(nt * 3);
        double inv = 1.0 / weld, w2 = weld * weld;
        int Weld(double x, double y, double z)
        {
            long ix = (long)Math.Floor(x * inv), iy = (long)Math.Floor(y * inv), iz = (long)Math.Floor(z * inv);
            if (map.TryGetValue((ix, iy, iz), out int id)) { return id; }
            for (long a = -1; a <= 1; a++)
                for (long b = -1; b <= 1; b++)
                    for (long c = -1; c <= 1; c++)
                    {
                        if (a == 0 && b == 0 && c == 0) continue;
                        if (!map.TryGetValue((ix + a, iy + b, iz + c), out int j)) continue;
                        double dx = vx[3 * j] - x, dy = vx[3 * j + 1] - y, dz = vx[3 * j + 2] - z;
                        if (dx * dx + dy * dy + dz * dz <= w2) { m.Welded++; return j; }
                    }
            id = vx.Count / 3;
            vx.Add(x); vx.Add(y); vx.Add(z);
            map[(ix, iy, iz)] = id;
            return id;
        }
        var tri = new List<int>(nt * 3);
        var tl = new List<int>(nt);
        for (int t = 0; t < nt; t++)
        {
            int o = 9 * t;
            int a = Weld(s.P[o], s.P[o + 1], s.P[o + 2]), b = Weld(s.P[o + 3], s.P[o + 4], s.P[o + 5]), c = Weld(s.P[o + 6], s.P[o + 7], s.P[o + 8]);
            if (a == b || b == c || a == c) { m.Degenerate++; continue; }
            // 넓이 0(한 줄) — 0.1 mm × 1 µm 미만
            double ux = vx[3 * b] - vx[3 * a], uy = vx[3 * b + 1] - vx[3 * a + 1], uz = vx[3 * b + 2] - vx[3 * a + 2];
            double qx = vx[3 * c] - vx[3 * a], qy = vx[3 * c + 1] - vx[3 * a + 1], qz = vx[3 * c + 2] - vx[3 * a + 2];
            double cx = uy * qz - uz * qy, cy = uz * qx - ux * qz, cz = ux * qy - uy * qx;
            if (cx * cx + cy * cy + cz * cz < 4e-20) { m.Degenerate++; continue; }
            tri.Add(a); tri.Add(b); tri.Add(c); tl.Add(s.L[t]);
        }
        m.V = vx.ToArray();
        m.T = tri.ToArray();
        int n = tl.Count;
        // ② 부재 = 같은 레이어 · 꼭짓점을 함께 쓰는 삼각형 덩어리(합집합 찾기)
        var par = new int[n];
        for (int i = 0; i < n; i++) par[i] = i;
        int Find(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
        var first = new Dictionary<long, int>(n * 2);
        for (int t = 0; t < n; t++)
            for (int k = 0; k < 3; k++)
            {
                long key = (long)m.T[3 * t + k] * 65536L + tl[t];
                if (first.TryGetValue(key, out int u)) { int ra = Find(t), rb = Find(u); if (ra != rb) par[ra] = rb; }
                else first[key] = t;
            }
        var root2part = new Dictionary<int, int>();
        m.TriPart = new int[n];
        for (int t = 0; t < n; t++)
        {
            int r = Find(t);
            if (!root2part.TryGetValue(r, out int p))
            {
                p = m.Parts.Count; root2part[r] = p;
                string ln = tl[t] < m.Layers.Count ? m.Layers[tl[t]] : "";
                m.Parts.Add(new StructPart { Layer = tl[t], Hatch = !StructUnits.NoHatch(ln), Excluded = StructUnits.IsTerrain(ln) });
            }
            m.TriPart[t] = p;
            var P = m.Parts[p]; P.Tris++;
            for (int k = 0; k < 3; k++)
            {
                int v = m.T[3 * t + k];
                double x = m.V[3 * v], y = m.V[3 * v + 1], z = m.V[3 * v + 2];
                if (x < P.MinX) P.MinX = x; if (x > P.MaxX) P.MaxX = x;
                if (y < P.MinY) P.MinY = y; if (y > P.MaxY) P.MaxY = y;
                if (z < P.MinZ) P.MinZ = z; if (z > P.MaxZ) P.MaxZ = z;
            }
        }
        // ③ 모서리 검사(부재 안) — 정방향 · 역방향 쓰인 수
        var edges = new Dictionary<(int, int, int), (int F, int B)>(n * 3);
        for (int t = 0; t < n; t++)
        {
            int p = m.TriPart[t];
            for (int k = 0; k < 3; k++)
            {
                int a = m.T[3 * t + k], b = m.T[3 * t + (k + 1) % 3];
                var key = (p, Math.Min(a, b), Math.Max(a, b));
                edges.TryGetValue(key, out var fb);
                edges[key] = a < b ? (fb.F + 1, fb.B) : (fb.F, fb.B + 1);
            }
        }
        foreach (var kv in edges)
        {
            var P = m.Parts[kv.Key.Item1];
            int tot = kv.Value.F + kv.Value.B;
            if ((tot & 1) == 1) P.Open++;
            else if (tot > 2) P.NonManifold++;
            else if (kv.Value.F != kv.Value.B) P.Misoriented++;
        }
        // ④ 부호 부피 · 닫힌 부재만 뒤집기(검토 낮음 — 열린 부재의 부피 부호는 믿지 않는다)
        for (int t = 0; t < n; t++)
        {
            int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
            double ax = m.V[3 * a], ay = m.V[3 * a + 1], az = m.V[3 * a + 2];
            double bx = m.V[3 * b], by = m.V[3 * b + 1], bz = m.V[3 * b + 2];
            double cx = m.V[3 * c], cy = m.V[3 * c + 1], cz = m.V[3 * c + 2];
            m.Parts[m.TriPart[t]].Volume += (ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)) / 6.0;
        }
        // ★ 속 빈 솔리드의 빈칸 껍질(닫힘 · 음의 부피 · 꼭짓점이 바깥 껍질과 안 이어짐)은 그것을 품은 같은 레이어 부재에 붙인다 — 혼자 두면 «뒤집힌 부재»로 뒤집혀 빈칸이 메워진다
        var merge = new int[m.Parts.Count];
        for (int i = 0; i < merge.Length; i++) merge[i] = i;
        List<int>[]? triOf = null;
        // ★[검토 중간] 상자만 보면 고리 벽 안(벽 밖 — 가운데 빈 땅)의 뒤집힌 부재도 «빈칸»으로 붙어 단면에서 사라진다 — 꼭짓점 하나로 위로 쏜 반직선 짝홀(안에 있나)까지 본다
        bool Inside(int q, int p)
        {
            if (triOf == null) { triOf = new List<int>[m.Parts.Count]; for (int k = 0; k < triOf.Length; k++) triOf[k] = new List<int>(); for (int t = 0; t < n; t++) triOf[m.TriPart[t]].Add(t); }
            int t0 = triOf[p][0], v0 = m.T[3 * t0];
            double px = m.V[3 * v0] + 1.37e-7, py = m.V[3 * v0 + 1] + 2.91e-7, pz = m.V[3 * v0 + 2];
            int cr = 0;
            foreach (int t in triOf[q])
            {
                int a = m.T[3 * t], b = m.T[3 * t + 1], c = m.T[3 * t + 2];
                double ax = m.V[3 * a], ay = m.V[3 * a + 1], bx = m.V[3 * b], by = m.V[3 * b + 1], cx = m.V[3 * c], cy = m.V[3 * c + 1];
                double d = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Math.Abs(d) < 1e-14) continue;
                double l1 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / d, l2 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                double z = l1 * m.V[3 * a + 2] + l2 * m.V[3 * b + 2] + l3 * m.V[3 * c + 2];
                if (z > pz) cr++;
            }
            return (cr & 1) == 1;
        }
        for (int i = 0; i < m.Parts.Count; i++)
        {
            var P = m.Parts[i];
            if (!(P.Closed && P.Misoriented == 0 && P.Volume < 0)) continue;
            int best = -1; double bv = double.MaxValue; const double e = 1e-6;
            for (int j = 0; j < m.Parts.Count; j++)
            {
                var Q = m.Parts[j];
                if (j == i || Q.Layer != P.Layer || !(Q.Volume > -P.Volume) || Q.Volume >= bv) continue;
                if (Q.MinX <= P.MinX + e && Q.MinY <= P.MinY + e && Q.MinZ <= P.MinZ + e && Q.MaxX >= P.MaxX - e && Q.MaxY >= P.MaxY - e && Q.MaxZ >= P.MaxZ - e && Inside(j, i)) { best = j; bv = Q.Volume; }
            }
            if (best >= 0) merge[i] = best;
        }
        if (merge.Where((x, i) => x != i).Any())
        {
            var keep = new List<StructPart>(); var newIx = new int[m.Parts.Count];
            for (int i = 0; i < m.Parts.Count; i++) if (merge[i] == i) { newIx[i] = keep.Count; keep.Add(m.Parts[i]); }
            for (int i = 0; i < m.Parts.Count; i++)
            {
                if (merge[i] == i) continue;
                var P = m.Parts[i]; var Q = m.Parts[merge[i]];
                Q.Tris += P.Tris; Q.Volume += P.Volume; Q.Open += P.Open; Q.NonManifold += P.NonManifold; Q.Misoriented += P.Misoriented;
                newIx[i] = newIx[merge[i]];
            }
            for (int t = 0; t < n; t++) m.TriPart[t] = newIx[m.TriPart[t]];
            m.Parts = keep;
        }
        foreach (var P in m.Parts)
            if (P.Closed && P.Misoriented == 0 && P.Volume < 0) { P.Flipped = true; P.Volume = -P.Volume; m.Flipped++; }
        if (m.Flipped > 0)
            for (int t = 0; t < n; t++)
                if (m.Parts[m.TriPart[t]].Flipped) (m.T[3 * t + 1], m.T[3 * t + 2]) = (m.T[3 * t + 2], m.T[3 * t + 1]);
        m.Reindex();
        return m;
    }

    /// <summary>원지반 상자 절반 이상을 덮는 부재(지형으로 내보낸 것)를 뺀다(D6) — 뺀 수. 상자는 세계 좌표.</summary>
    public int ExcludeCovering(double gx0, double gy0, double gx1, double gy1)
    {
        double ga = Math.Max(0, gx1 - gx0) * Math.Max(0, gy1 - gy0);
        if (!(ga > 0)) return 0;
        int k = 0;
        foreach (var P in Parts)
        {
            if (P.Excluded) continue;
            double a = Math.Max(0, P.MaxX - P.MinX) * Math.Max(0, P.MaxY - P.MinY);
            if (a >= 0.5 * ga) { P.Excluded = true; k++; }
        }
        if (k > 0) Reindex();
        return k;
    }

    /// <summary>뺀 부재가 바뀌면 상자 · 격자를 다시.</summary>
    internal void Reindex()
    {
        MinX = MinY = MinZ = double.MaxValue; MaxX = MaxY = MaxZ = double.MinValue;
        foreach (var P in Parts)
        {
            if (P.Excluded) continue;
            MinX = Math.Min(MinX, P.MinX); MinY = Math.Min(MinY, P.MinY); MinZ = Math.Min(MinZ, P.MinZ);
            MaxX = Math.Max(MaxX, P.MaxX); MaxY = Math.Max(MaxY, P.MaxY); MaxZ = Math.Max(MaxZ, P.MaxZ);
        }
        int n = TriCount;
        _stamp = new int[n]; _stampN = 0;
        if (Empty) { Gnx = Gny = 0; GStart = new int[1]; GItems = Array.Empty<int>(); return; }
        double w = Math.Max(1e-3, MaxX - MinX), h = Math.Max(1e-3, MaxY - MinY);
        int live = 0;
        for (int t = 0; t < n; t++) if (!Parts[TriPart[t]].Excluded) live++;
        double c = Math.Max(0.25, Math.Sqrt(w * h / Math.Max(1, live / 4.0)));
        // 큰 삼각형이 셀을 너무 많이 먹으면 셀을 키운다(등록 수 ≤ 8n + 10만)
        for (int it = 0; it < 12; it++)
        {
            long reg = 0;
            for (int t = 0; t < n && reg <= 8L * live + 100000; t++)
            {
                if (Parts[TriPart[t]].Excluded) continue;
                TriBox(t, out double x0, out double y0, out double x1, out double y1);
                reg += (long)(Math.Floor((x1 - MinX) / c) - Math.Floor((x0 - MinX) / c) + 1) * (long)(Math.Floor((y1 - MinY) / c) - Math.Floor((y0 - MinY) / c) + 1);
            }
            if (reg <= 8L * live + 100000) break;
            c *= 2;
        }
        Gc = c; Gx0 = MinX; Gy0 = MinY;
        Gnx = Math.Max(1, (int)Math.Floor(w / c) + 1); Gny = Math.Max(1, (int)Math.Floor(h / c) + 1);
        int nc = Gnx * Gny;
        var cnt = new int[nc];
        int[] pos = Array.Empty<int>();
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                GStart = new int[nc + 1];
                for (int i = 0; i < nc; i++) GStart[i + 1] = GStart[i] + cnt[i];
                GItems = new int[GStart[nc]];
                pos = (int[])GStart.Clone();
            }
            for (int t = 0; t < n; t++)
            {
                if (Parts[TriPart[t]].Excluded) continue;
                TriBox(t, out double x0, out double y0, out double x1, out double y1);
                int i0 = Cx(x0), i1 = Cx(x1), j0 = Cy(y0), j1 = Cy(y1);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        int cell = j * Gnx + i;
                        if (pass == 0) cnt[cell]++;
                        else GItems[pos[cell]++] = t;
                    }
            }
        }
    }

    int Cx(double x) => Math.Clamp((int)Math.Floor((x - Gx0) / Gc), 0, Gnx - 1);
    int Cy(double y) => Math.Clamp((int)Math.Floor((y - Gy0) / Gc), 0, Gny - 1);

    internal void TriBox(int t, out double x0, out double y0, out double x1, out double y1)
    {
        int a = T[3 * t], b = T[3 * t + 1], c = T[3 * t + 2];
        x0 = Math.Min(V[3 * a], Math.Min(V[3 * b], V[3 * c])); x1 = Math.Max(V[3 * a], Math.Max(V[3 * b], V[3 * c]));
        y0 = Math.Min(V[3 * a + 1], Math.Min(V[3 * b + 1], V[3 * c + 1])); y1 = Math.Max(V[3 * a + 1], Math.Max(V[3 * b + 1], V[3 * c + 1]));
    }

    /// <summary>국소 XY 선분(끝없는 직선이면 미리 상자로 잘라 넘긴다) 곁 셀의 삼각형(뺀 부재 없음 · 겹침 없음).</summary>
    internal List<int> AlongLine(double x0, double y0, double x1, double y1, List<int>? into = null)
    {
        into ??= new List<int>();
        into.Clear();
        if (Gnx == 0) return into;
        if (++_stampN == int.MaxValue) { Array.Clear(_stamp); _stampN = 1; }
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int steps = Math.Max(1, (int)Math.Ceiling(len / Gc));
        double e = 1e-6;
        for (int s = 0; s < steps; s++)
        {
            double ta = (double)s / steps, tb = (double)(s + 1) / steps;
            double ax = x0 + (x1 - x0) * ta, ay = y0 + (y1 - y0) * ta, bx = x0 + (x1 - x0) * tb, by = y0 + (y1 - y0) * tb;
            int i0 = Cx(Math.Min(ax, bx) - e), i1 = Cx(Math.Max(ax, bx) + e), j0 = Cy(Math.Min(ay, by) - e), j1 = Cy(Math.Max(ay, by) + e);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int cell = j * Gnx + i;
                    for (int k = GStart[cell]; k < GStart[cell + 1]; k++)
                    {
                        int t = GItems[k];
                        if (_stamp[t] == _stampN) continue;
                        _stamp[t] = _stampN;
                        into.Add(t);
                    }
                }
        }
        return into;
    }

    /// <summary>계측 한 줄(부재 · 삼각형 · 열린/비다양체/어긋난 · 뒤집음 · 용접 · 퇴화 · 뺀 부재 · 못 쓴 개체).</summary>
    public string Summary()
        => $"부재 {Parts.Count(p => !p.Excluded)} · 삼각형 {TriCount}(들어옴 {InTris} · 퇴화 {Degenerate} · 용접 {Welded}) · 열린 {OpenParts} · 비다양체 {NonManifoldParts} · 어긋난 {MisorientedParts} · 뒤집음 {Flipped}"
         + (Parts.Count(p => p.Excluded) > 0 ? $" · 뺀 부재 {Parts.Count(p => p.Excluded)}" : "") + (Unused > 0 ? $" · 못 쓴 개체 {Unused}" : "");
}
