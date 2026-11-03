using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace DH.Grading.Core;

/// <summary>★★★[v107 · 계획_v107.md 3 · 검토 중간 6 · 8 · 9] <b>[3D 보기] 지형 메시</b> — 정지면 세모를 구분(원지반 그대로 · 계획면 · 소단 · 사면 · 옹벽 자리 · 옹벽 면 · 정지)마다 묶는다.
/// <list type="bullet">
/// <item>원지반 그대로 = <b>세 점 모두</b> 원지반과 <see cref="Input.Tol"/>(1 cm) 안(무게중심으로 가르면 붙여넣기가 다시 나눈 자리에 «정지» 점박이가 생긴다 — 검토 중간 6).</item>
/// <item>바뀐 세모는 무게중심으로: 옹벽 변환 자리 · 3D 옹벽 발자국(가파르면 옹벽 면 — 3D 옹벽이 보이면 숨김) → 소단 → 사면(절/성) → 계획면 → 정지. 다각형은 InfraWorks SHP와 같은 것.</item>
/// <item>점은 <b>구분마다 따로</b>(경계에서 꺾여 보이게 — 뷰어는 색인 그대로 법선만 셈 · 검토 중간 9) · 세모는 구분 차례로 · 절성토 깊이(점마다 정지면 − 원지반 · 원지반 없으면 NaN).</item>
/// <item>바뀐 칸(2 m 격자) — 원지반 층은 이 칸 안 세모만 보낸다(지형과 겹쳐 깜빡이지 않게 · 크기 한도 — 검토 중간 8).</item>
/// </list></summary>
public static class TerrainMesh
{
    /// <summary>★[v111.8 · 계획_v111_8.md 6장 · JACK 1102 «차로 = 아스팔트 · 길어깨 = 콘크리트»] 진입도로 구분 둘(차로 · 길어깨)은 <b>맨 끝</b>에 — 옛 번호 그대로(옛 뷰어 · S175 옛 답).</summary>
    public enum Cls : byte { Ground, Plan, Bench, SlopeCut, SlopeFill, WallZone, WallFace, Other, Road, Shoulder }
    public static readonly string[] Keys = { "ground", "plan", "bench", "slopeCut", "slopeFill", "wallZone", "wallFace", "other", "road", "shoulder" };
    public static readonly string[] Names = { "원지반", "계획면", "소단", "사면(절토)", "사면(성토)", "옹벽 자리", "옹벽 면", "정지", "진입도로", "길어깨" };

    public sealed class Input
    {
        /// <summary>정지면 점(도면 좌표) · 세모(0부터 · 셋씩).</summary>
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), Z = Array.Empty<double>();
        public int[] F = Array.Empty<int>();
        /// <summary>원지반 높이(없으면 null).</summary>
        public Func<double, double, double?>? GroundZ;
        public Geometry? Plan, Bench, SlopeCut, SlopeFill, WallMask, WallFoot;
        public double Tol = 0.01;
        /// <summary>가파름 — 세모 법선 |nz|가 이보다 작으면(약 65° 넘게 섬) 옹벽 면 후보.</summary>
        public double SteepNz = 0.42;
        public double Cell = 2.0;
        /// <summary>★[v111.8] 진입도로 자리 — 도로마다 넓힌 발자국(F ⊕ ε · 이음 띠 포함)의 합. null = 도로 없음(옛 규칙 그대로).
        /// 무게중심이 이 안이면 부지 다각형보다 먼저 가른다: 포장(<see cref="RoadPave"/>) → 차로(<see cref="RoadLane"/>) / 길어깨 · 그 밖 = 원지반 그대로 → 옹벽 자리(가파름) → 소단(<see cref="RoadBench"/> 띠 안 · 없으면 거의 평평) → 사면(절/성 — <see cref="BaseZ"/> 기준).</summary>
        public Geometry? RoadArea;
        /// <summary>★[v111.8] 설계 포장(차로 + 길어깨 — 노선에서) · 차로만(차로 끝까지). 차로가 null이면 포장 전체가 «진입도로».</summary>
        public Geometry? RoadPave, RoadLane;
        /// <summary>★[v111.8] 정지면 높이(도로 사면 절/성 가름 — 무게중심 높이 ≥ 이것이면 성토) · null이면 원지반으로.</summary>
        public Func<double, double, double?>? BaseZ;
        /// <summary>★[v111.8] 도로 자리 «소단» — 경사(높이/수평)가 이보다 작으면(1:10). <see cref="RoadBench"/>가 없을 때만(옛 규칙).</summary>
        public double BenchSlope = 0.1;
        /// <summary>★★[v111.8.1 · JACK 1103 «진입도로의 소단이 안 보여»] 도로 엔진의 소단 띠(<see cref="RoadBenches.Strips"/> — 계단선 짝 사이 횡단으로 평평한 띠).
        /// 있으면(비어 있어도) 도로 자리 소단은 이 띠 안만 — 도로 소단은 종단 경사(최대 24.9 % 실측)를 따라가 기울기로는 못 가른다.
        /// 또 이 규칙에서는 도로 자리 안이라도 세 점 모두 정지면(<see cref="BaseZ"/>) 그대로인 세모(이음 띠 · 부지 계획면)는 부지 규칙으로 간다. null = 옛 규칙 그대로.</summary>
        public Geometry? RoadBench;
    }

    public sealed class Output
    {
        public float[] Pos = Array.Empty<float>();
        public int[] Idx = Array.Empty<int>();
        public float[] Cf = Array.Empty<float>();
        /// <summary>(구분, 첫 세모, 세모 수) — 세모는 구분 차례로.</summary>
        public readonly List<(Cls Cls, int Start, int Count)> Ranges = new();
        public readonly int[] Count = new int[Keys.Length];
        /// <summary>바뀐 칸 — 원점 · 칸 · 너비 · 높이 · 표시.</summary>
        public double GX0, GY0, GCell; public int GW, GH; public bool[] GMark = Array.Empty<bool>();
        /// <summary>바뀐 자리 범위(도면 좌표 · 없으면 NaN) — 위성 «자세히» 범위.</summary>
        public double CMinX = double.NaN, CMinY = double.NaN, CMaxX = double.NaN, CMaxY = double.NaN;
        public int NoGround;

        /// <summary>바뀐 칸 안인가(둘레 한 칸 더).</summary>
        public bool InChanged(double x, double y)
        {
            if (GMark.Length == 0) return false;
            int i = (int)Math.Floor((x - GX0) / GCell), j = (int)Math.Floor((y - GY0) / GCell);
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= GW || b >= GH) continue;
                    if (GMark[b * GW + a]) return true;
                }
            return false;
        }
    }

    /// <summary>★[v111.8.1 · JACK 1103 시공 순서 «진입도로부터»] 시공 재생 격자 마디 표시 — 마디 (x0 + i·cell, y0 + j·cell)가 <paramref name="area"/> 안(경계 포함)이면 1 · 차례 = j·(w+1) + i(zf · zg와 같음).</summary>
    public static byte[] GridMask(Geometry area, double x0, double y0, double cell, int w, int h, out int count)
    {
        var m = new byte[(w + 1) * (h + 1)];
        count = 0;
        if (area == null || area.IsEmpty) return m;
        var pa = PreparedGeometryFactory.Prepare(area);
        var gf = NtsSupport.Factory();
        for (int j = 0; j <= h; j++)
            for (int i = 0; i <= w; i++)
                if (pa.Covers(gf.CreatePoint(new Coordinate(x0 + i * cell, y0 + j * cell)))) { m[j * (w + 1) + i] = 1; count++; }
        return m;
    }

    public static Output Build(Input inp, double ox, double oy)
    {
        var o = new Output();
        int nV = inp.X.Length, nT = inp.F.Length / 3;
        var cf = new double[nV];
        for (int i = 0; i < nV; i++)
        {
            var g = inp.GroundZ?.Invoke(inp.X[i], inp.Y[i]);
            cf[i] = g.HasValue ? inp.Z[i] - g.Value : double.NaN;
            if (!g.HasValue) o.NoGround++;
        }
        IPreparedGeometry? P(Geometry? g) => g == null || g.IsEmpty ? null : PreparedGeometryFactory.Prepare(g);
        var pMask = P(inp.WallMask); var pFoot = P(inp.WallFoot); var pBench = P(inp.Bench);
        var pCut = P(inp.SlopeCut); var pFill = P(inp.SlopeFill); var pPlan = P(inp.Plan);
        var pRoad = P(inp.RoadArea); var pPave = P(inp.RoadPave); var pLane = P(inp.RoadLane);
        var pRBench = P(inp.RoadBench);
        bool benchRule = inp.RoadBench != null;
        var gf = NtsSupport.Factory();
        var cls = new Cls[nT];
        double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
        // ★[설치 전 검토 낮음] 원지반이 없으면 «바뀜»을 못 잰다 — 다각형(SHP · 옹벽 자리)으로만 가르고 그 밖은 원지반
        bool noGround = inp.GroundZ == null;
        for (int t = 0; t < nT; t++)
        {
            int a = inp.F[t * 3], b = inp.F[t * 3 + 1], c = inp.F[t * 3 + 2];
            bool changed = noGround || Changed(cf[a], inp.Tol) || Changed(cf[b], inp.Tol) || Changed(cf[c], inp.Tol);
            Cls k;
            // ★[v111.8 · 계획 6장] 진입도로 자리 먼저(부지 다각형보다 · 포장은 땅과 같은 높이여도 «진입도로» — 위성으로 보이면 «도로가 끊김»으로 읽힘)
            if (pRoad != null)
            {
                double rx = (inp.X[a] + inp.X[b] + inp.X[c]) / 3, ry = (inp.Y[a] + inp.Y[b] + inp.Y[c]) / 3;
                var rp = gf.CreatePoint(new Coordinate(rx, ry));
                if (pRoad.Contains(rp) && !(benchRule && OnBase(a) && OnBase(b) && OnBase(c) && !(pPave != null && pPave.Contains(rp))))
                {
                    if (pPave != null && pPave.Contains(rp)) k = pLane == null || pLane.Contains(rp) ? Cls.Road : Cls.Shoulder;
                    else if (!changed) k = Cls.Ground;
                    else
                    {
                        double ux0 = inp.X[b] - inp.X[a], uy0 = inp.Y[b] - inp.Y[a], uz0 = inp.Z[b] - inp.Z[a];
                        double vx0 = inp.X[c] - inp.X[a], vy0 = inp.Y[c] - inp.Y[a], vz0 = inp.Z[c] - inp.Z[a];
                        double nx0 = uy0 * vz0 - uz0 * vy0, ny0 = uz0 * vx0 - ux0 * vz0, nz0 = ux0 * vy0 - uy0 * vx0, nl0 = Math.Sqrt(nx0 * nx0 + ny0 * ny0 + nz0 * nz0);
                        if (nl0 > 0 && Math.Abs(nz0) / nl0 < inp.SteepNz) k = Cls.WallZone;                          // 옹벽 자리 · 가파른 띠(도로 옹벽 3D 몸통은 없음)
                        else if (benchRule ? pRBench != null && pRBench.Contains(rp)                                             // ★[v111.8.1] 엔진 소단 띠 → 소단(쇄석)
                                           : nz0 != 0 && Math.Sqrt(nx0 * nx0 + ny0 * ny0) / Math.Abs(nz0) < inp.BenchSlope) k = Cls.Bench;   // 옛 규칙 — 거의 평평 → 소단
                        else
                        {
                            double cz = (inp.Z[a] + inp.Z[b] + inp.Z[c]) / 3;
                            double? bzv = inp.BaseZ != null ? inp.BaseZ(rx, ry) : inp.GroundZ?.Invoke(rx, ry);
                            k = bzv == null || cz >= bzv.Value ? Cls.SlopeFill : Cls.SlopeCut;
                        }
                    }
                    cls[t] = k;
                    if (k != Cls.Ground) Grow(a, b, c);
                    continue;
                }
            }
            if (!changed) { cls[t] = Cls.Ground; continue; }
            double cx = (inp.X[a] + inp.X[b] + inp.X[c]) / 3, cy = (inp.Y[a] + inp.Y[b] + inp.Y[c]) / 3;
            var pt = gf.CreatePoint(new Coordinate(cx, cy));
            double ux = inp.X[b] - inp.X[a], uy = inp.Y[b] - inp.Y[a], uz = inp.Z[b] - inp.Z[a];
            double vx = inp.X[c] - inp.X[a], vy = inp.Y[c] - inp.Y[a], vz = inp.Z[c] - inp.Z[a];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            bool steep = nl > 0 && Math.Abs(nz) / nl < inp.SteepNz;
            if (steep && pFoot != null && pFoot.Contains(pt)) k = Cls.WallFace;
            else if (pMask != null && pMask.Contains(pt)) k = steep ? Cls.WallFace : Cls.WallZone;
            else if (pBench != null && pBench.Contains(pt)) k = Cls.Bench;
            else if (pCut != null && pCut.Contains(pt)) k = Cls.SlopeCut;
            else if (pFill != null && pFill.Contains(pt)) k = Cls.SlopeFill;
            else if (pPlan != null && pPlan.Contains(pt)) k = Cls.Plan;
            else k = noGround ? Cls.Ground : Cls.Other;
            // 옹벽 면은 3D 옹벽 발자국 안일 때만 숨김 후보 — 발자국 밖 가파른 옹벽 자리는 «옹벽 자리»로 보이게(3D가 없는 벽에 구멍이 나지 않게)
            if (k == Cls.WallFace && (pFoot == null || !pFoot.Contains(pt))) k = Cls.WallZone;
            cls[t] = k;
            if (k == Cls.Ground) continue;
            Grow(a, b, c);
        }
        // ★[v111.8.1] 정지면 그대로인 점(도로 자리 이음 띠 — 부지 쪽 세모)
        bool OnBase(int v) { var bz = inp.BaseZ?.Invoke(inp.X[v], inp.Y[v]); return bz != null && Math.Abs(inp.Z[v] - bz.Value) <= inp.Tol; }
        void Grow(int a, int b, int c)
        {
            mnx = Math.Min(mnx, Math.Min(inp.X[a], Math.Min(inp.X[b], inp.X[c]))); mxx = Math.Max(mxx, Math.Max(inp.X[a], Math.Max(inp.X[b], inp.X[c])));
            mny = Math.Min(mny, Math.Min(inp.Y[a], Math.Min(inp.Y[b], inp.Y[c]))); mxy = Math.Max(mxy, Math.Max(inp.Y[a], Math.Max(inp.Y[b], inp.Y[c])));
        }
        if (mnx <= mxx) { o.CMinX = mnx; o.CMinY = mny; o.CMaxX = mxx; o.CMaxY = mxy; }

        // 바뀐 칸
        if (mnx <= mxx)
        {
            o.GCell = inp.Cell; o.GX0 = mnx - inp.Cell; o.GY0 = mny - inp.Cell;
            o.GW = Math.Max(1, (int)Math.Ceiling((mxx - mnx) / inp.Cell) + 3); o.GH = Math.Max(1, (int)Math.Ceiling((mxy - mny) / inp.Cell) + 3);
            o.GMark = new bool[o.GW * o.GH];
            for (int t = 0; t < nT; t++)
            {
                if (cls[t] == Cls.Ground) continue;
                int a = inp.F[t * 3], b = inp.F[t * 3 + 1], c = inp.F[t * 3 + 2];
                int i0 = (int)Math.Floor((Math.Min(inp.X[a], Math.Min(inp.X[b], inp.X[c])) - o.GX0) / o.GCell);
                int i1 = (int)Math.Floor((Math.Max(inp.X[a], Math.Max(inp.X[b], inp.X[c])) - o.GX0) / o.GCell);
                int j0 = (int)Math.Floor((Math.Min(inp.Y[a], Math.Min(inp.Y[b], inp.Y[c])) - o.GY0) / o.GCell);
                int j1 = (int)Math.Floor((Math.Max(inp.Y[a], Math.Max(inp.Y[b], inp.Y[c])) - o.GY0) / o.GCell);
                for (int j = Math.Max(0, j0); j <= Math.Min(o.GH - 1, j1); j++)
                    for (int i = Math.Max(0, i0); i <= Math.Min(o.GW - 1, i1); i++) o.GMark[j * o.GW + i] = true;
            }
        }

        // 구분 차례로 · 점은 구분마다 따로
        var pos = new List<float>(nV * 3 + 1024);
        var cfo = new List<float>(nV + 1024);
        var idx = new List<int>(nT * 3);
        var map = new int[nV];
        Array.Fill(map, -1);
        var touched = new List<int>();
        for (int k = 0; k < Keys.Length; k++)
        {
            int start = idx.Count / 3;
            foreach (int v in touched) map[v] = -1;
            touched.Clear();
            for (int t = 0; t < nT; t++)
            {
                if ((int)cls[t] != k) continue;
                // 위에서 보아 반시계(겉 = 위) — LandXML 면 차례는 Civil 것이라 정해져 있지 않다
                int f0 = inp.F[t * 3], f1 = inp.F[t * 3 + 1], f2 = inp.F[t * 3 + 2];
                double cz = (inp.X[f1] - inp.X[f0]) * (inp.Y[f2] - inp.Y[f0]) - (inp.Y[f1] - inp.Y[f0]) * (inp.X[f2] - inp.X[f0]);
                if (cz < 0) (f1, f2) = (f2, f1);
                foreach (int v in new[] { f0, f1, f2 })
                {
                    if (map[v] < 0)
                    {
                        map[v] = pos.Count / 3; touched.Add(v);
                        pos.Add((float)(inp.X[v] - ox)); pos.Add((float)(inp.Y[v] - oy)); pos.Add((float)inp.Z[v]);
                        cfo.Add((float)cf[v]);
                    }
                    idx.Add(map[v]);
                }
            }
            int cnt = idx.Count / 3 - start;
            o.Count[k] = cnt;
            if (cnt > 0) o.Ranges.Add(((Cls)k, start, cnt));
        }
        o.Pos = pos.ToArray(); o.Idx = idx.ToArray(); o.Cf = cfo.ToArray();
        return o;
    }

    static bool Changed(double d, double tol) => double.IsFinite(d) && Math.Abs(d) > tol;

    /// <summary>원지반 층 — 세모 무더기(도면 좌표) 중 바뀐 칸 안(무게중심 · 점 하나라도)만 · 같은 점 합침(1 mm) · 원점 뺀 float.</summary>
    public static (float[] Pos, int[] Idx) Clip(IEnumerable<(Point3 A, Point3 B, Point3 C)> tris, Output o, double ox, double oy)
    {
        var pos = new List<float>(); var idx = new List<int>();
        var weld = new Dictionary<(long, long, long), int>();
        int V(Point3 p)
        {
            var k = ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
            if (weld.TryGetValue(k, out int i)) return i;
            i = pos.Count / 3; weld[k] = i;
            pos.Add((float)(p.X - ox)); pos.Add((float)(p.Y - oy)); pos.Add((float)p.Z);
            return i;
        }
        foreach (var (a, b, c) in tris)
        {
            double cx = (a.X + b.X + c.X) / 3, cy = (a.Y + b.Y + c.Y) / 3;
            if (!(o.InChanged(cx, cy) || o.InChanged(a.X, a.Y) || o.InChanged(b.X, b.Y) || o.InChanged(c.X, c.Y))) continue;
            int ia = V(a), ib = V(b), ic = V(c);
            if (ia == ib || ib == ic || ic == ia) continue;
            double cz = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (cz < 0) (ib, ic) = (ic, ib);                                        // 위에서 보아 반시계
            idx.Add(ia); idx.Add(ib); idx.Add(ic);
        }
        return (pos.ToArray(), idx.ToArray());
    }
}
