namespace DH.Grading.Core;

/// <summary>★★[v107 · 설치 전 검토 중간 «DH3D가 도면을 바꾼다»] <b>두 TIN 사이 절토 · 성토량</b> — Civil 임시 체적면(TinVolumeSurface) 없이 셈한다.
/// <para>정의는 Civil «체적면»과 같다: 두 면이 다 있는 자리에서 ∫ max(0, 비교 − 기준) = 성토 · ∫ max(0, 기준 − 비교) = 절토. 비교면 세모 × 기준면 세모를 평면에서 서로 자른 볼록 조각마다
/// 두 면이 모두 1차식이므로 높이차도 1차식 — 조각을 부채꼴 세모로 나눠 0선으로 쪼개 정확히 적분한다(근사 없음).</para>
/// <para>[3D 보기]는 도면을 읽기만 해야 한다 — 임시 체적면은 트랜잭션을 되돌려도(Abort) 도면을 «바뀜»으로 만든다(화면 없는 Civil 실측 DBMOD 0 → 1).</para></summary>
public static class TinVolume
{
    public readonly record struct Result(double Cut, double Fill, double Area, int Pairs);

    /// <summary><paramref name="cmp"/>(비교 = 정지면) − <paramref name="bas"/>(기준 = 원지반). 세모는 도면 좌표 — 안에서 원점을 빼 셈한다.</summary>
    public static Result Compute(IReadOnlyList<(Point3 A, Point3 B, Point3 C)> cmp, IReadOnlyList<(Point3 A, Point3 B, Point3 C)> bas)
    {
        if (cmp.Count == 0 || bas.Count == 0) return new Result(0, 0, 0, 0);
        double ox = cmp[0].A.X, oy = cmp[0].A.Y;
        // 기준면 세모 — 평면식 z = a·x + b·y + c(원점 뺀 좌표) · 상자 · 격자
        int nb = bas.Count;
        var bx = new double[nb * 3]; var by = new double[nb * 3];
        var pa = new double[nb]; var pb = new double[nb]; var pc = new double[nb];
        var ok = new bool[nb];
        double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue, sumSz = 0;
        for (int i = 0; i < nb; i++)
        {
            var (A, B, C) = bas[i];
            bx[i * 3] = A.X - ox; by[i * 3] = A.Y - oy; bx[i * 3 + 1] = B.X - ox; by[i * 3 + 1] = B.Y - oy; bx[i * 3 + 2] = C.X - ox; by[i * 3 + 2] = C.Y - oy;
            ok[i] = Plane(bx[i * 3], by[i * 3], A.Z, bx[i * 3 + 1], by[i * 3 + 1], B.Z, bx[i * 3 + 2], by[i * 3 + 2], C.Z, out pa[i], out pb[i], out pc[i]);
            if (!ok[i]) continue;
            for (int k = 0; k < 3; k++) { mnx = Math.Min(mnx, bx[i * 3 + k]); mxx = Math.Max(mxx, bx[i * 3 + k]); mny = Math.Min(mny, by[i * 3 + k]); mxy = Math.Max(mxy, by[i * 3 + k]); }
            sumSz += Math.Max(Math.Max(bx[i * 3], Math.Max(bx[i * 3 + 1], bx[i * 3 + 2])) - Math.Min(bx[i * 3], Math.Min(bx[i * 3 + 1], bx[i * 3 + 2])),
                              Math.Max(by[i * 3], Math.Max(by[i * 3 + 1], by[i * 3 + 2])) - Math.Min(by[i * 3], Math.Min(by[i * 3 + 1], by[i * 3 + 2])));
        }
        if (!(mnx <= mxx)) return new Result(0, 0, 0, 0);
        double cell = Math.Max(0.5, 2 * sumSz / nb);
        int gw = Math.Max(1, (int)Math.Ceiling((mxx - mnx) / cell) + 1), gh = Math.Max(1, (int)Math.Ceiling((mxy - mny) / cell) + 1);
        while ((long)gw * gh > 20_000_000) { cell *= 1.5; gw = (int)Math.Ceiling((mxx - mnx) / cell) + 1; gh = (int)Math.Ceiling((mxy - mny) / cell) + 1; }
        var grid = new List<int>?[gw * gh];
        for (int i = 0; i < nb; i++)
        {
            if (!ok[i]) continue;
            int i0 = (int)((Math.Min(bx[i * 3], Math.Min(bx[i * 3 + 1], bx[i * 3 + 2])) - mnx) / cell), i1 = (int)((Math.Max(bx[i * 3], Math.Max(bx[i * 3 + 1], bx[i * 3 + 2])) - mnx) / cell);
            int j0 = (int)((Math.Min(by[i * 3], Math.Min(by[i * 3 + 1], by[i * 3 + 2])) - mny) / cell), j1 = (int)((Math.Max(by[i * 3], Math.Max(by[i * 3 + 1], by[i * 3 + 2])) - mny) / cell);
            for (int j = j0; j <= j1; j++) for (int q = i0; q <= i1; q++) (grid[j * gw + q] ??= new List<int>(4)).Add(i);
        }

        double cut = 0, fill = 0, area = 0; int pairs = 0;
        var seen = new HashSet<int>();
        var poly = new List<(double X, double Y)>(9); var tmp = new List<(double X, double Y)>(9);
        foreach (var (A, B, C) in cmp)
        {
            double ax = A.X - ox, ay = A.Y - oy, bxx = B.X - ox, byy = B.Y - oy, cx = C.X - ox, cy = C.Y - oy;
            if (!Plane(ax, ay, A.Z, bxx, byy, B.Z, cx, cy, C.Z, out double ta, out double tb, out double tc)) continue;
            double tx0 = Math.Min(ax, Math.Min(bxx, cx)), tx1 = Math.Max(ax, Math.Max(bxx, cx)), ty0 = Math.Min(ay, Math.Min(byy, cy)), ty1 = Math.Max(ay, Math.Max(byy, cy));
            if (tx1 < mnx || tx0 > mxx || ty1 < mny || ty0 > mxy) continue;
            int i0 = Math.Max(0, (int)((tx0 - mnx) / cell)), i1 = Math.Min(gw - 1, (int)((tx1 - mnx) / cell));
            int j0 = Math.Max(0, (int)((ty0 - mny) / cell)), j1 = Math.Min(gh - 1, (int)((ty1 - mny) / cell));
            seen.Clear();
            for (int j = j0; j <= j1; j++)
                for (int q = i0; q <= i1; q++)
                {
                    var lst = grid[j * gw + q];
                    if (lst == null) continue;
                    foreach (int s in lst)
                    {
                        if (!seen.Add(s)) continue;
                        // 비교 세모를 기준 세모(반시계로)의 세 반평면으로 자른다
                        poly.Clear(); poly.Add((ax, ay)); poly.Add((bxx, byy)); poly.Add((cx, cy));
                        double sx0 = bx[s * 3], sy0 = by[s * 3], sx1 = bx[s * 3 + 1], sy1 = by[s * 3 + 1], sx2 = bx[s * 3 + 2], sy2 = by[s * 3 + 2];
                        if ((sx1 - sx0) * (sy2 - sy0) - (sy1 - sy0) * (sx2 - sx0) < 0) { (sx1, sx2) = (sx2, sx1); (sy1, sy2) = (sy2, sy1); }
                        if (!Clip(poly, tmp, sx0, sy0, sx1, sy1) || !Clip(poly, tmp, sx1, sy1, sx2, sy2) || !Clip(poly, tmp, sx2, sy2, sx0, sy0)) continue;
                        // 높이차 d = 비교 − 기준(둘 다 1차식) — 부채꼴 세모마다 양 · 음 몫
                        double D((double X, double Y) p) => (ta * p.X + tb * p.Y + tc) - (pa[s] * p.X + pb[s] * p.Y + pc[s]);
                        double d0 = D(poly[0]); bool any = false;
                        for (int k = 1; k + 1 < poly.Count; k++)
                        {
                            double ar = 0.5 * Math.Abs((poly[k].X - poly[0].X) * (poly[k + 1].Y - poly[0].Y) - (poly[k].Y - poly[0].Y) * (poly[k + 1].X - poly[0].X));
                            if (ar <= 0) continue;
                            any = true;
                            area += ar;
                            var (pos, neg) = Split(ar, d0, D(poly[k]), D(poly[k + 1]));
                            fill += pos; cut += neg;
                        }
                        if (any) pairs++;
                    }
                }
        }
        return new Result(cut, fill, area, pairs);
    }

    /// <summary>세 점 평면 z = a·x + b·y + c(위에서 보아 넓이 0이면 거짓 — 선 세모는 부피 없음).</summary>
    static bool Plane(double x0, double y0, double z0, double x1, double y1, double z1, double x2, double y2, double z2, out double a, out double b, out double c)
    {
        double ux = x1 - x0, uy = y1 - y0, uz = z1 - z0, vx = x2 - x0, vy = y2 - y0, vz = z2 - z0;
        double nz = ux * vy - uy * vx;
        a = b = c = 0;
        if (Math.Abs(nz) < 1e-12) return false;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz;
        a = -nx / nz; b = -ny / nz; c = z0 - a * x0 - b * y0;
        return true;
    }

    /// <summary>볼록 다각형을 반평면(변 p→q의 왼쪽)으로 자른다 — 남은 것이 세 점 미만이면 거짓.</summary>
    static bool Clip(List<(double X, double Y)> poly, List<(double X, double Y)> tmp, double px, double py, double qx, double qy)
    {
        tmp.Clear();
        double ex = qx - px, ey = qy - py;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            double sa = ex * (a.Y - py) - ey * (a.X - px), sb = ex * (b.Y - py) - ey * (b.X - px);
            if (sa >= 0) tmp.Add(a);
            if ((sa >= 0) != (sb >= 0))
            {
                double t = sa / (sa - sb);
                tmp.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
        }
        poly.Clear(); poly.AddRange(tmp);
        return poly.Count >= 3;
    }

    /// <summary>1차식 d가 세 꼭짓점 값 (d0, d1, d2)인 세모(넓이 ar)의 ∫max(0,d) · ∫max(0,−d).</summary>
    public static (double Pos, double Neg) Split(double ar, double d0, double d1, double d2)
    {
        double total = ar * (d0 + d1 + d2) / 3;
        int np = (d0 > 0 ? 1 : 0) + (d1 > 0 ? 1 : 0) + (d2 > 0 ? 1 : 0);
        if (np == 0) return (0, -total);
        if (np == 3 || (d0 >= 0 && d1 >= 0 && d2 >= 0)) return (total, 0);
        double pos;
        if (np == 1)
        {
            // 양수 꼭짓점 하나 — 그 꼭짓점에서 0선까지의 작은 세모
            double p = d0 > 0 ? d0 : d1 > 0 ? d1 : d2;
            double r = d0 > 0 ? d1 : d1 > 0 ? d0 : d0, s = d0 > 0 ? d2 : d1 > 0 ? d2 : d1;
            pos = ar * (p / (p - r)) * (p / (p - s)) * p / 3;
        }
        else
        {
            // 음수 꼭짓점 하나 — 음 몫을 같은 법으로 구해 뺀다
            double m = d0 <= 0 ? d0 : d1 <= 0 ? d1 : d2;
            double r = d0 <= 0 ? d1 : d1 <= 0 ? d0 : d0, s = d0 <= 0 ? d2 : d1 <= 0 ? d2 : d1;
            double neg = m < 0 ? ar * (m / (m - r)) * (m / (m - s)) * (-m) / 3 : 0;
            return (total + neg, neg);
        }
        return (pos, pos - total);
    }
}
