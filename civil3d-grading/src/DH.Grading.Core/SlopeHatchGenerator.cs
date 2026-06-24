namespace DH.Grading.Core;

/// <summary>
/// 평면도용 사면 노리선(법면 표시) 생성. 각 사면 1단의 상단 모서리를 따라
/// 짧은선(절반 길이)·긴선(전체 길이)을 사면 방향으로 긋는다.
///   - 짧은선: shortSpacing(기본 1m)마다, 길이 = 사면폭 × 0.5
///   - 긴선:   longSpacing(기본 5m)마다, 길이 = 사면폭 전체(상단→하단)
/// daylight 바깥(넉넉히 뻗은 과확장)은 제외하고, daylight 걸친 단은 지반선에서 끊는다.
/// 순수 함수(AutoCAD 의존성 없음) → 단위테스트 가능.
/// </summary>
public static class SlopeHatchGenerator
{
    /// <summary>평면폭/높이 비가 이보다 작으면 수직 옹벽으로 보고 노리선을 그리지 않는다(구배 0 등).</summary>
    private const double WallRatio = 0.1;


    /// <summary>노리선 선분 목록 (평면, z는 상단 모서리 표고). 각 항목 = (시작=상단점, 끝).</summary>
    public static List<(Point3 A, Point3 B)> Generate(
        GradingBreaklineResult result, IGroundSurface ground,
        double shortSpacing = 1.0, double longSpacing = 5.0)
    {
        var ticks = new List<(Point3, Point3)>();
        if (shortSpacing <= 0) shortSpacing = 1.0;
        if (longSpacing <= 0) longSpacing = 5.0;

        var radials = result.Breaklines
            .Where(b => b.Kind == BreaklineKind.Radial)
            .Select(b => b.Points)
            .ToList();
        int n = radials.Count;
        if (n < 2) return ticks;

        int ratio = Math.Max(1, (int)Math.Round(longSpacing / shortSpacing)); // 몇 번째마다 긴선인지
        int maxLen = radials.Max(p => p.Count);

        // 사면 1단 = 프로파일 인덱스 (2k, 2k+1). (홀수 구간은 소단=평탄 → 제외)
        for (int k = 0; 2 * k + 1 < maxLen; k++)
        {
            int topIdx = 2 * k, botIdx = 2 * k + 1;
            var top = new List<Point3?>(n);
            var bot = new List<Point3?>(n);

            for (int v = 0; v < n; v++)
            {
                var prof = radials[v];
                if (botIdx >= prof.Count) { top.Add(null); bot.Add(null); continue; }
                int sgn = Math.Sign(SafeDiff(ground, prof[0]));
                if (sgn == 0) { top.Add(null); bot.Add(null); continue; }

                var tp = prof[topIdx];
                var bp = prof[botIdx];
                double dz = Math.Abs(tp.Z - bp.Z);
                // 평탄 구간(소단)에는 노리선 금지
                if (dz < 1e-6) { top.Add(null); bot.Add(null); continue; }
                // 수직 옹벽(구배≈0, 평면폭/높이 < 0.1)에는 노리선 금지 — 경사 사면만 표현
                if (Dist2D(tp, bp) / dz < WallRatio) { top.Add(null); bot.Add(null); continue; }
                // 상단이 지반 위(구성측)로 떠 있는 사면만 — daylight 너머 과확장 단 제외
                if (SafeDiff(ground, tp) * sgn <= 1e-6) { top.Add(null); bot.Add(null); continue; }
                // 하단이 지반을 넘으면(daylight 걸친 단) toe=지반선에서 끊기
                if (Math.Sign(SafeDiff(ground, bp)) == -sgn)
                    bp = GroundCross(tp, bp, ground, sgn);

                top.Add(tp); bot.Add(bp);
            }

            // 끊기지 않은 연속 구간(run)마다 호 길이를 따라 노리선 배치
            int i = 0;
            while (i < n)
            {
                if (top[i] is null) { i++; continue; }
                int j = i;
                var runTop = new List<Point3>();
                var runBot = new List<Point3>();
                while (j < n && top[j] is not null) { runTop.Add(top[j]!.Value); runBot.Add(bot[j]!.Value); j++; }
                EmitRun(runTop, runBot, shortSpacing, ratio, ticks);
                i = j;
            }
        }
        return ticks;
    }

    private static void EmitRun(List<Point3> top, List<Point3> bot, double step, int ratio, List<(Point3, Point3)> ticks)
    {
        if (top.Count < 2) return;
        var cum = new double[top.Count];
        for (int i = 1; i < top.Count; i++) cum[i] = cum[i - 1] + Dist2D(top[i - 1], top[i]);
        double total = cum[^1];
        if (total < 1e-9) return;

        int count = 0;
        for (double d = 0; d <= total + 1e-9; d += step, count++)
        {
            int seg = 0;
            while (seg < top.Count - 2 && cum[seg + 1] < d) seg++;
            double segLen = cum[seg + 1] - cum[seg];
            double t = segLen < 1e-9 ? 0 : (d - cum[seg]) / segLen;

            var tp = Lerp(top[seg], top[seg + 1], t);
            var bp = Lerp(bot[seg], bot[seg + 1], t);
            if (Dist2D(tp, bp) < 0.02) continue; // 미세 노리선(daylight 끝자락) 제거. 수직옹벽(≥0.05)은 유지
            bool isLong = count % ratio == 0;
            double frac = isLong ? 1.0 : 0.5;

            // 평면 노리선: 상단점 → (사면 방향으로 frac만큼), z는 상단 표고로 평탄
            var end = new Point3(tp.X + (bp.X - tp.X) * frac, tp.Y + (bp.Y - tp.Y) * frac, tp.Z);
            ticks.Add((new Point3(tp.X, tp.Y, tp.Z), end));
        }
    }

    /// <summary>tp→bp 사이에서 정지면이 지반과 만나는 점(평면 보간) — daylight toe.</summary>
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
                double f = pa == pd ? 0 : pa / (pa - pd);
                double tt = ((s - 1) + f) / sub;
                return Lerp(a, b, tt);
            }
            pa = pd;
        }
        return b;
    }

    private static double SafeDiff(IGroundSurface g, Point3 p)
        => g.TryGetElevation(p.X, p.Y, out double e) ? p.Z - e : 0;

    private static Point3 Lerp(Point3 a, Point3 b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static double Dist2D(Point3 a, Point3 b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
