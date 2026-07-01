namespace DH.Grading.Core;

/// <summary>
/// 평면도용 사면 노리선(법면 표시) 생성 — 동심 링(GradingGeometry.Build의 result.Rings) 기반.
/// 사면 1단의 상단(crest) 모서리를 따라 사면 방향으로 선을 긋는다.
///   · 긴선: longSpacing(기본 5m)마다, 길이 = 사면폭 전체(상단→하단/지반).
///   · 짧은선: shortSpacing(기본 1m)마다, 길이 = 사면폭의 절반.
/// daylight 너머(원지반을 넘어선 오버사이즈)는 제외하고, daylight 걸친 단은 지반선에서 끊는다.
/// 소단(berm) 모서리 선도 별도로 반환. 순수 함수(AutoCAD 비의존).
/// </summary>
public static class SlopeHatchGenerator
{
    /// <summary>평면폭/높이 비가 이보다 작으면 수직 옹벽으로 보고 노리선 생략(구배 0 등).</summary>
    private const double WallRatio = 0.1;

    /// <summary>
    /// 노리선 선분(Ticks: 상단점→끝, z=상단표고)과 소단선(BenchLines: 폴리라인)을 만든다.
    /// up=true(절토)면 구성측=지반 아래, up=false(성토)면 지반 위.
    /// </summary>
    public static (List<(Point3 A, Point3 B)> Ticks, List<List<Point3>> BenchLines) Generate(
        IReadOnlyList<IReadOnlyList<Point3>> rings, IGroundSurface ground, bool up,
        double shortSpacing = 1.0, double longSpacing = 5.0)
    {
        var ticks = new List<(Point3, Point3)>();
        var benchLines = new List<List<Point3>>();
        if (rings == null || rings.Count < 2) return (ticks, benchLines);
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;
        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing)); // 몇 번째마다 긴선
        int sgn = up ? -1 : +1; // 구성측 부호(절토=지반아래, 성토=지반위)

        // 사면 페이스 = (rings[2k], rings[2k+1]). crest=높은 Z.
        for (int k = 0; 2 * k + 1 < rings.Count; k++)
        {
            var rA = rings[2 * k]; var rB = rings[2 * k + 1];
            if (rA.Count < 2 || rB.Count < 2) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB;
            var other = aHigher ? rB : rA;
            EmitFaceTicks(crest, other, ground, sgn, shortSpacing, ratio, ticks);
        }

        // 소단(berm) 모서리 = (rings[2k+1], rings[2k+2]) 두 링의 구성측 run.
        for (int k = 0; 2 * k + 2 < rings.Count; k++)
        {
            AddRealRuns(rings[2 * k + 1], ground, sgn, benchLines);
            AddRealRuns(rings[2 * k + 2], ground, sgn, benchLines);
        }
        return (ticks, benchLines);
    }

    private static void EmitFaceTicks(IReadOnlyList<Point3> crest, IReadOnlyList<Point3> other,
        IGroundSurface ground, int sgn, double step, int ratio, List<(Point3, Point3)> ticks)
    {
        var cum = new double[crest.Count];
        for (int i = 1; i < crest.Count; i++) cum[i] = cum[i - 1] + Dist2D(crest[i - 1], crest[i]);
        double total = cum[^1];
        if (total < 1e-9) return;

        int count = 0;
        for (double d = 0; d <= total + 1e-9; d += step, count++)
        {
            var cp = PointAtDist(crest, cum, d);
            if (Math.Sign(SafeDiff(ground, cp)) != sgn) continue; // crest가 구성측(원지반 안쪽)일 때만
            var op = NearestOnRing(other, cp);
            double dz = Math.Abs(cp.Z - op.Z);
            if (dz < 1e-6) continue;                              // 평탄(소단) 아님
            if (Dist2D(cp, op) / dz < WallRatio) continue;       // 수직 옹벽 제외
            var eff = op;
            if (Math.Sign(SafeDiff(ground, op)) == -sgn)         // 하단이 지반 넘으면 toe에서 끊기
                eff = GroundCross(cp, op, ground, sgn);
            if (Dist2D(cp, eff) < 0.02) continue;                // 미세 노리선 제거
            double frac = (count % ratio == 0) ? 1.0 : 0.5;      // 긴선/짧은선
            var end = new Point3(cp.X + (eff.X - cp.X) * frac, cp.Y + (eff.Y - cp.Y) * frac, cp.Z);
            ticks.Add((new Point3(cp.X, cp.Y, cp.Z), end));
        }
    }

    /// <summary>링을 구성측(원지반 안쪽) 연속 구간으로 쪼개 폴리라인으로 추가.</summary>
    private static void AddRealRuns(IReadOnlyList<Point3> ring, IGroundSurface ground, int sgn, List<List<Point3>> outLines)
    {
        if (ring.Count < 2) return;
        List<Point3>? run = null;
        foreach (var p in ring)
        {
            bool real = Math.Sign(SafeDiff(ground, p)) == sgn;
            if (real) { (run ??= new List<Point3>()).Add(p); }
            else if (run != null) { if (run.Count >= 2) outLines.Add(run); run = null; }
        }
        if (run != null && run.Count >= 2) outLines.Add(run);
    }

    /// <summary>a→b 사이에서 정지면이 지반과 만나는 점(평면 보간) — daylight toe.</summary>
    private static Point3 GroundCross(Point3 a, Point3 b, IGroundSurface g, int sgn)
    {
        int sub = 8;
        double pa = SafeDiff(g, a);
        for (int s = 1; s <= sub; s++)
        {
            double t = (double)s / sub;
            var p = Lerp(a, b, t);
            double pd = SafeDiff(g, p);
            if (Math.Sign(pd) == -sgn)
            {
                double f = Math.Abs(pa - pd) < 1e-12 ? 0 : pa / (pa - pd);
                double tt = ((s - 1) + f) / sub;
                return Lerp(a, b, tt);
            }
            pa = pd;
        }
        return b;
    }

    private static Point3 NearestOnRing(IReadOnlyList<Point3> ring, Point3 q)
    {
        Point3 best = ring[0]; double bestD = double.MaxValue;
        foreach (var p in ring)
        {
            double dx = p.X - q.X, dy = p.Y - q.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private static Point3 PointAtDist(IReadOnlyList<Point3> ring, double[] cum, double d)
    {
        int seg = 0;
        while (seg < ring.Count - 2 && cum[seg + 1] < d) seg++;
        double segLen = cum[seg + 1] - cum[seg];
        double t = segLen < 1e-9 ? 0 : (d - cum[seg]) / segLen;
        return Lerp(ring[seg], ring[seg + 1], t);
    }

    private static double AvgZ(IReadOnlyList<Point3> ring)
    {
        double s = 0; foreach (var p in ring) s += p.Z; return s / Math.Max(ring.Count, 1);
    }

    private static double SafeDiff(IGroundSurface g, Point3 p)
        => g.TryGetElevation(p.X, p.Y, out double e) ? p.Z - e : 0;

    private static Point3 Lerp(Point3 a, Point3 b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static double Dist2D(Point3 a, Point3 b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
