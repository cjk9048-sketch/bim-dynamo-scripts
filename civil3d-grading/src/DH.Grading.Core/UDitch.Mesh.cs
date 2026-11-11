namespace DH.Grading.Core;

public static partial class UDitch
{
    /// <summary>닫힌 덩어리 하나 — 꼭짓점(도면 좌표 x, y, z) · 면(네모 · 세모 · 바깥 감김) · 부피(㎥) · 부분 이름(«본체» · «기초»).</summary>
    public sealed class Solid
    {
        public string Part = "";
        public readonly List<(double X, double Y, double Z)> V = new();
        public readonly List<int[]> F = new();
        public double Volume;
    }

    /// <summary>M06 — 측구 3D 덩어리(본체 · 기초). 단면 = <see cref="DitchSection.U"/>(가운데 맞춤) · 연직 단면을 평면 마디마다 맞댄 이음(꺾인 점 = 두 변의 이등분 면 · 옆 거리는 1/cos로 늘려 두 변 모두 옆 거리 그대로)
    /// · 바닥고 = 측점 비례(한 경사) · 양 끝 마구리. 좌우 대칭 단면이라 부피 = 단면적 × 중심선 길이(S260 UD가 잰다). 못 지으면 빈 목록 + 까닭.</summary>
    public static List<Solid> Meshes(Ditch d, Settings s, out string why)
    {
        why = "";
        var res = new List<Solid>();
        var pr = ProfileOf(d, s);
        if (pr.Error.Length > 0) { why = pr.Error; return res; }
        var p = CleanPts(d.Pts);
        int n = p.Count;
        var sh = DitchSection.U(d.Size.Dims);
        if (sh.Warn.Length > 0) { why = "단면 — " + sh.Warn; return res; }
        double W = d.Size.B + 2 * d.Size.Tw, H = d.Size.H;
        // 마디마다 옆 방향(왼쪽 +) — 끝은 그 변의 법선 · 가운데는 이등분(늘림 포함)
        var side = new (double X, double Y)[n];
        for (int k = 0; k < n; k++)
        {
            (double X, double Y) Nrm(int a, int b)
            {
                double dx = p[b][0] - p[a][0], dy = p[b][1] - p[a][1], l = Math.Sqrt(dx * dx + dy * dy);
                return (-dy / l, dx / l);
            }
            if (k == 0) side[k] = Nrm(0, 1);
            else if (k == n - 1) side[k] = Nrm(n - 2, n - 1);
            else
            {
                var a = Nrm(k - 1, k); var b = Nrm(k, k + 1);
                double mx = a.X + b.X, my = a.Y + b.Y, ml = Math.Sqrt(mx * mx + my * my);
                mx /= ml; my /= ml;
                double c = mx * a.X + my * a.Y;   // cos(꺾임/2)
                side[k] = (mx / c, my / c);
            }
        }
        foreach (var (poly0, part) in sh.Concrete.Select((q, i) => (q, i == 0 ? "본체" : "기초")))
        {
            // 단면 (옆 거리, 높이 — 바닥고 기준) · 반시계로
            var poly = poly0.Select(q => (L: q.O - W / 2, Z: q.Z + H)).ToList();
            double sa = 0;
            for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; sa += a.L * b.Z - b.L * a.Z; }
            if (sa < 0) poly.Reverse();
            int m = poly.Count;
            var sol = new Solid { Part = part };
            for (int k = 0; k < n; k++)
                foreach (var q in poly)
                    sol.V.Add((p[k][0] + side[k].X * q.L, p[k][1] + side[k].Y * q.L, pr.Inv[k] + q.Z));
            // 옆면 — 평평한 네모는 그대로 · 경사진 측구의 꺾인 마디 사이 윗/아랫면은 살짝 비틀린 네모라 세모 둘로(Civil 솔리드는 평평한 면만 받음 ·
            //   대각선은 옆 거리 작은 쪽 시점 → 큰 쪽 종점으로 한 가지 — 같은 폭의 위 · 아래 면 오차가 서로 지움). 부피는 넓이 × 길이와 경사 · 꺾임만큼 아주 조금 다름(S260 ⑦ 재봄)
            for (int k = 0; k + 1 < n; k++)
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    int a = k * m + i, b = k * m + j, c = (k + 1) * m + j, e = (k + 1) * m + i;
                    if (Planar(sol.V[a], sol.V[b], sol.V[c], sol.V[e])) { sol.F.Add(new[] { a, b, c, e }); continue; }
                    if (poly[i].L < poly[j].L) { sol.F.Add(new[] { a, b, c }); sol.F.Add(new[] { a, c, e }); }
                    else { sol.F.Add(new[] { a, b, e }); sol.F.Add(new[] { b, c, e }); }
                }
            var tri = EarClip(poly);
            if (tri.Count != m - 2) { why = $"{part} 단면을 세모로 못 나눔"; return new List<Solid>(); }
            foreach (var (a, b, c) in tri)
            {
                sol.F.Add(new[] { a, c, b });                                   // 시점 마구리
                sol.F.Add(new[] { (n - 1) * m + a, (n - 1) * m + b, (n - 1) * m + c });   // 종점 마구리
            }
            // 감김은 부피 부호로 맞춤(바깥 = +)
            double v = SignedVolume(sol);
            if (v < 0) { for (int f = 0; f < sol.F.Count; f++) Array.Reverse(sol.F[f]); v = -v; }
            sol.Volume = v;
            if (v < 1e-9) { why = $"{part} 부피 0"; return new List<Solid>(); }
            res.Add(sol);
        }
        return res;
    }

    /// <summary>네 점이 한 평면(1 µm 안)인가 — 첫 점 기준(대좌표).</summary>
    static bool Planar((double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c, (double X, double Y, double Z) d)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nl < 1e-15) return false;
        return Math.Abs(((d.X - a.X) * nx + (d.Y - a.Y) * ny + (d.Z - a.Z) * nz) / nl) < 1e-6;
    }

    /// <summary>닫힌 면 부피(발산 정리 · 면을 부채꼴 세모로 · 첫 꼭짓점 기준이라 대좌표에도 맞음).</summary>
    public static double SignedVolume(Solid s)
    {
        if (s.V.Count == 0) return 0;
        var o = s.V[0];
        double v = 0;
        foreach (var f in s.F)
            for (int i = 1; i + 1 < f.Length; i++)
            {
                var a = s.V[f[0]]; var b = s.V[f[i]]; var c = s.V[f[i + 1]];
                double ax = a.X - o.X, ay = a.Y - o.Y, az = a.Z - o.Z, bx = b.X - o.X, by = b.Y - o.Y, bz = b.Z - o.Z, cx = c.X - o.X, cy = c.Y - o.Y, cz = c.Z - o.Z;
                v += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
            }
        return v / 6;
    }

    /// <summary>반시계 단순 다각형 → 세모(귀 자르기 · 꼭짓점 몇 개짜리 단면 용).</summary>
    public static List<(int A, int B, int C)> EarClip(IReadOnlyList<(double L, double Z)> poly)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList();
        var o = new List<(int, int, int)>();
        double Cr((double L, double Z) a, (double L, double Z) b, (double L, double Z) c) => (b.L - a.L) * (c.Z - a.Z) - (b.Z - a.Z) * (c.L - a.L);
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool cut = false;
            for (int t = 0; t < idx.Count; t++)
            {
                int ia = idx[(t + idx.Count - 1) % idx.Count], ib = idx[t], ic = idx[(t + 1) % idx.Count];
                var a = poly[ia]; var b = poly[ib]; var c = poly[ic];
                if (Cr(a, b, c) <= 1e-12) continue;   // 오목 · 일직선
                bool inside = false;
                foreach (int q in idx)
                {
                    if (q == ia || q == ib || q == ic) continue;
                    var x = poly[q];
                    if (Cr(a, b, x) >= -1e-12 && Cr(b, c, x) >= -1e-12 && Cr(c, a, x) >= -1e-12) { inside = true; break; }
                }
                if (inside) continue;
                o.Add((ia, ib, ic));
                idx.RemoveAt(t);
                cut = true;
                break;
            }
            if (!cut) break;
        }
        if (idx.Count == 3) o.Add((idx[0], idx[1], idx[2]));
        return o;
    }

    /// <summary>[3D 보기] 메시 — 본체 + 기초를 세모로(장면 좌표 = 도면 − (ox, oy)). 못 지으면 빈 것.</summary>
    public static (float[] Pos, int[] Idx) SceneMesh(Ditch d, Settings s, double ox, double oy)
    {
        var pos = new List<float>(); var idx = new List<int>();
        foreach (var sol in Meshes(d, s, out _))
        {
            int b = pos.Count / 3;
            foreach (var v in sol.V) { pos.Add((float)(v.X - ox)); pos.Add((float)(v.Y - oy)); pos.Add((float)v.Z); }
            foreach (var f in sol.F)
                for (int i = 1; i + 1 < f.Length; i++) { idx.Add(b + f[0]); idx.Add(b + f[i]); idx.Add(b + f[i + 1]); }
        }
        return (pos.ToArray(), idx.ToArray());
    }
}
