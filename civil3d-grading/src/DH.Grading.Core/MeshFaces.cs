namespace DH.Grading.Core;

/// <summary>★★[v117.4 · JACK 1111 «불러온 벽 솔리드 한 면에 대각선(얇은 쐐기)»] 같은 평면 세모를 <b>n각 면 하나</b>로 합친다 — 솔리드로 바꿀 메시(StructSolids)용.
/// <para><b>잰 원인</b>: JACK IFC(우리 Revit 애드인 판)는 닫힘 · T이음 0 · 바늘 0이고, 같은 면 이웃 세모의 잔차(맞은편 꼭짓점 ~ 이웃 평면)는
/// IFC 자기 좌표에서 1.4e-14 m인데 대지 배치(회전 + 수십만 m 이동)를 거친 세계 좌표에서 <b>3.0e-9 m</b>까지 커진다(수십만 m의 끝자리 ~1e-10 m가 길쭉한 세모에서 불어남 — 시험 S250 ①b가 재현).
/// 세모마다 면을 주면 ConvertToSolid(같은 평면 합침)가 그 어긋남을 다른 평면으로 보고 일부를 안 합친다(대각선) — ACIS 문턱은 Civil 밖에서 못 재 추정.</para>
/// <para><b>고침</b>: 합칠지를 ACIS에 맡기지 않는다 — 평면마다 면 하나를 준다. 변으로 이웃한 면을 씨앗(가장 넓은 면) 평면에서 <see cref="PlaneTol"/> 안 ·
/// 같은 방향이면 한 무리로 묶고, 무리의 경계(짝 없는 방향 변)를 고리 하나로 잇는다. 고리가 둘 이상(구멍) · 꼬인 마디면 그 무리는 원래 면 그대로.
/// 문턱 1 µm = 잰 잔차의 300배 · 진짜 꺾임(Revit 면)보다 한참 작다. 경계 위 꼭짓점(곧은 변 위 점 포함)은 다 남겨 이웃 면과 꼭 맞물린다(닫힘 그대로).</para></summary>
public static class MeshFaces
{
    /// <summary>같은 평면으로 볼 거리(m).</summary>
    public const double PlaneTol = 1e-6;

    public sealed class Result
    {
        /// <summary>합친 면(꼭짓점 색인 = <see cref="Verts"/> 안 차례 · 감김 그대로).</summary>
        public List<int[]> Faces = new();
        /// <summary>남은 꼭짓점 → 원래 색인(면 안쪽에만 있던 점은 빠짐 — 떠도는 점 없음).</summary>
        public List<int> Verts = new();
        public int In, Groups, Kept;
        /// <summary>합친 무리 꼭짓점 ~ 그 무리 평면 최대 거리(m).</summary>
        public double MaxRes;
        /// <summary>같은 방향 변 겹침(비다양체) — 손대지 않고 그대로 돌려줌.</summary>
        public bool Bail;
        public string Text => Bail ? $"면 합침 안 함(겹친 변) · 면 {In}"
            : $"면 {In} → {Faces.Count}(합친 평면 {Groups}" + (Kept > 0 ? $" · 구멍 · 꼬임이라 그대로 {Kept}" : "") + $" · 평면 어긋남 최대 {MaxRes:0.#E+0} m)";
    }

    /// <summary>면 목록(세모 · n각)을 평면마다 하나로. <paramref name="v"/>는 국소 좌표(큰 좌표면 빼고 줄 것).</summary>
    public static Result MergeCoplanar(IReadOnlyList<Point3> v, IReadOnlyList<int[]> faces, double tol = PlaneTol)
    {
        var R = new Result { In = faces.Count };
        int nf = faces.Count;
        var edge = new Dictionary<(int, int), int>();
        bool bad = false;
        for (int f = 0; f < nf && !bad; f++)
        {
            var F = faces[f];
            for (int i = 0; i < F.Length; i++)
                if (!edge.TryAdd((F[i], F[(i + 1) % F.Length]), f)) { bad = true; break; }
        }
        if (bad)
        {
            R.Bail = true;
            var keep = new List<int[]>(faces.Select(F => (int[])F.Clone()));
            Compact(v.Count, keep, R);
            return R;
        }
        // 넓이 벡터(Newell · 면 첫 점 기준 — 큰 좌표 소거 오차 없게)
        var N = new (double X, double Y, double Z)[nf];
        var A = new double[nf];
        for (int f = 0; f < nf; f++)
        {
            var F = faces[f]; var o = v[F[0]];
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < F.Length; i++)
            {
                var a = v[F[i]]; var b = v[F[(i + 1) % F.Length]];
                double ax = a.X - o.X, ay = a.Y - o.Y, az = a.Z - o.Z, bx = b.X - o.X, by = b.Y - o.Y, bz = b.Z - o.Z;
                nx += (ay - by) * (az + bz); ny += (az - bz) * (ax + bx); nz += (ax - bx) * (ay + by);
            }
            N[f] = (nx, ny, nz); A[f] = 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
        }
        const double Tiny = 1e-12;   // 넓이 0 면(바늘) — 방향을 못 믿으니 거리로만
        var grp = new int[nf]; Array.Fill(grp, -1);
        var groups = new List<List<int>>();
        foreach (int seed in Enumerable.Range(0, nf).OrderByDescending(i => A[i]))
        {
            if (grp[seed] >= 0) continue;
            int g = groups.Count; var mem = new List<int> { seed }; groups.Add(mem); grp[seed] = g;
            if (A[seed] < Tiny) continue;
            double l = 2 * A[seed]; double ux = N[seed].X / l, uy = N[seed].Y / l, uz = N[seed].Z / l;
            var o = v[faces[seed][0]];
            double D(int k) { var p = v[k]; return Math.Abs(ux * (p.X - o.X) + uy * (p.Y - o.Y) + uz * (p.Z - o.Z)); }
            bool Ok(int h)
            {
                if (A[h] >= Tiny && ux * N[h].X + uy * N[h].Y + uz * N[h].Z <= 0) return false;
                foreach (int k in faces[h]) if (D(k) > tol) return false;
                return true;
            }
            var st = new Stack<int>(); st.Push(seed);
            double res = 0;
            while (st.Count > 0)
            {
                var F = faces[st.Pop()];
                for (int i = 0; i < F.Length; i++)
                {
                    if (!edge.TryGetValue((F[(i + 1) % F.Length], F[i]), out int h) || grp[h] >= 0 || !Ok(h)) continue;
                    grp[h] = g; mem.Add(h); st.Push(h);
                    foreach (int k in faces[h]) res = Math.Max(res, D(k));
                }
            }
            if (mem.Count > 1) R.MaxRes = Math.Max(R.MaxRes, res);
        }
        var outF = new List<int[]>(groups.Count);
        foreach (var mem in groups)
        {
            if (mem.Count == 1) { outF.Add((int[])faces[mem[0]].Clone()); continue; }
            int g = grp[mem[0]];
            // 경계 = 짝(반대 방향 변)이 같은 무리에 없는 변
            var next = new Dictionary<int, int>(); bool ok = true; int nb = 0;
            foreach (int f in mem)
            {
                var F = faces[f];
                for (int i = 0; i < F.Length && ok; i++)
                {
                    int a = F[i], b = F[(i + 1) % F.Length];
                    if (edge.TryGetValue((b, a), out int h) && grp[h] == g) continue;
                    if (!next.TryAdd(a, b)) ok = false;   // 꼬인 마디(한 점에서 경계가 둘 나감)
                    nb++;
                }
            }
            var loop = new List<int>();
            if (ok && nb >= 3)
            {
                int s = next.Keys.First(), c = s;
                do { loop.Add(c); if (!next.TryGetValue(c, out c)) { ok = false; break; } } while (c != s && loop.Count <= nb);
                if (loop.Count != nb) ok = false;   // 고리가 둘 이상(구멍 · 떨어진 조각)
            }
            else ok = false;
            if (ok) { outF.Add(loop.ToArray()); R.Groups++; }
            else { R.Kept++; foreach (int f in mem) outF.Add((int[])faces[f].Clone()); }
        }
        Compact(v.Count, outF, R);
        return R;
    }

    /// <summary>★[계획 검토 MEDIUM 5] 닫힌 면 목록의 부호 부피(m³ · 평면 n각 면 그대로 — 면마다 (첫 점 · 넓이 벡터)/3). 만든 솔리드 부피와 견준다.</summary>
    public static double Volume(IReadOnlyList<Point3> v, IReadOnlyList<int[]> faces)
    {
        double vol = 0;
        foreach (var F in faces)
        {
            if (F.Length < 3) continue;
            var o = v[F[0]];
            for (int i = 1; i + 1 < F.Length; i++)
            {
                var b = v[F[i]]; var c = v[F[i + 1]];
                vol += o.X * (b.Y * c.Z - b.Z * c.Y) - o.Y * (b.X * c.Z - b.Z * c.X) + o.Z * (b.X * c.Y - b.Y * c.X);
            }
        }
        return vol / 6;
    }

    /// <summary>쓰인 꼭짓점만 남기고 색인을 다시 매김.</summary>
    static void Compact(int nv, List<int[]> faces, Result R)
    {
        var map = new int[nv]; Array.Fill(map, -1);
        foreach (var F in faces)
            for (int i = 0; i < F.Length; i++)
            {
                int k = F[i];
                if (map[k] < 0) { map[k] = R.Verts.Count; R.Verts.Add(k); }
                F[i] = map[k];
            }
        R.Faces = faces;
    }
}
