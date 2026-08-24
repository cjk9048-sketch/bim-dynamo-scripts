namespace DH.Grading.Core;

/// <summary>[역T형 옹벽 — JACK 0730 확정] 역T는 사용자가 고르지 않는다:
/// **계획폴리곤에 바로 붙어 서고(FromBench=0), 한 단 높이 안에서 원지반과 만나 그 위/아래 절성토가 없는
/// '순수 옹벽' 구간**만 자동으로 역T가 된다(정지옵션 형태 무시). 나머지 구간은 정지옵션 형태(보강토/앵커판넬).
/// 정렬선 = 계획경계 서브아크(경계 정점 보존 + 1m 보간), 벽 상/하단은 지반고를 따라 가변.</summary>
public static class WallTee
{
    /// <summary>역T 1런 — PathBottom=벽 전면 하단(절토=계획고, 성토=지반고), TopZ=정점별 벽 상단
    /// (절토=지반고, 성토=계획고), SoilLeft=+1이면 흙(뒷굽)이 진행방향 왼쪽.</summary>
    public readonly record struct Run(List<Point3> PathBottom, List<double> TopZ, int SoilLeft);

    /// <summary>zones 중 '순수 경계 옹벽'(FromBench==0 && 전 구간 벽높이 ≤ benchH+0.3) 자동 판정 →
    /// 역T 런들 + 역T로 전환된 구간 인덱스(스타일 생성에서 제외용) + 진단.</summary>
    public static (List<Run> Runs, List<int> TeeZoneIdx, string Diag) GenerateAuto(
        IReadOnlyList<Point3> boundary,
        IReadOnlyList<SlopeZone> zones,
        IGroundSurface ground, bool cut, double benchH, double minSlope = 0.05)
    {
        var runs = new List<Run>();
        var teeIdx = new List<int>();
        var sb = new System.Text.StringBuilder();
        if (boundary == null || boundary.Count < 3 || zones == null || ground == null)
            return (runs, teeIdx, "");
        double[] cum = GradingGeometry.CumLen2D(boundary);
        double total = cum[cum.Length - 1];
        if (total < 1e-6) return (runs, teeIdx, "");

        for (int zi = 0; zi < zones.Count; zi++)
        {
            var z = zones[zi];
            // [구간 구배 0804] 역T는 계획경계에 바로 붙는(0단부터) **수직** 구간만. 구배를 바꾼 구간은 사면이라 대상 아님.
            if (z == null || z.Rules.Count == 0) continue;
            if (z.Rules[0].FromBench != 0)
            { sb.Append($"구간{zi + 1}: {z.Rules[0].FromBench + 1}단부터라 역T 아님(스타일 옹벽) · "); continue; }
            if (z.Rules[0].Slope > minSlope + 1e-9)
            { sb.Append($"구간{zi + 1}: 1단 구배가 1:{z.Rules[0].Slope:0.###}(수직 아님) — 역T 아님 · "); continue; }

            // ★★[검토 0824 치명-3] **이 구간의 자로 읽는다.** T0/T1은 계획 폴리곤이 아니라
            //   그 구간이 들고 다니는 기준 폴리곤(그 단의 링) 위의 값일 수 있다(0824 개편).
            //   계획 폴리곤 호길이로 읽으면 둘레가 달라(178m vs 110m) 구간이 엉뚱한 변으로 감기고,
            //   teeIdx가 그 구간을 '역T 처리됨'으로 표시해 정상 옹벽 생성까지 통째로 빠진다.
            var zPoly = z.Ref ?? boundary;
            var zCum = z.RefCum ?? cum;
            double zTot = zCum[zCum.Length - 1];
            double t0 = z.T0, t1 = z.T1 >= z.T0 ? z.T1 : z.T1 + zTot;
            double arc = t1 - t0;
            if (arc < 0.5) continue;

            // 표본 파라미터: 구간 양끝 + 구간 내 경계 정점(코너 보존) + 1m 보간.
            var ts = new List<double> { t0, t1 };
            for (int i = 0; i < cum.Length - 1; i++)
            {
                double tv = cum[i];
                double tw = tv >= t0 ? tv : tv + total;   // 랩 보정
                if (tw > t0 + 1e-6 && tw < t1 - 1e-6) ts.Add(tw);
            }
            int nFill = (int)(arc / 1.0);
            for (int s = 1; s < nFill; s++) ts.Add(t0 + arc * s / nFill);
            ts.Sort();

            // 지반 표본 → 순수(1단 이내) 판정 + 경로/상하단 산출.
            var path = new List<Point3>();
            var topZ = new List<double>();
            bool pure = true; double hMax = 0; int miss = 0;
            foreach (var t in ts)
            {
                var (x, y, planZ) = PointAtParam(boundary, cum, t % total);
                if (!ground.TryGetElevation(x, y, out double g)) { miss++; continue; }
                double h = cut ? g - planZ : planZ - g;
                if (h < 0) h = 0;
                if (h > benchH + 0.3) { pure = false; break; }
                if (h > hMax) hMax = h;
                if (cut) { path.Add(new Point3(x, y, planZ)); topZ.Add(planZ + h); }
                else { path.Add(new Point3(x, y, planZ - h)); topZ.Add(planZ); }
            }
            if (!pure) { sb.Append($"구간{zi + 1}: 벽높이가 1단({benchH:F1}m) 초과 — 스타일 옹벽 유지 · "); continue; }
            if (path.Count < 2 || hMax < 0.3) { sb.Append($"구간{zi + 1}: 유효 벽 없음(높이 {hMax:F1}m·표본누락 {miss}) · "); continue; }

            int soilLeft = SoilLeftOf(path, boundary, cut);
            runs.Add(new Run(path, topZ, soilLeft));
            teeIdx.Add(zi);
            sb.Append($"구간{zi + 1}: 순수 1단(H≈{hMax:F1}m) → 역T 자동 · ");
        }
        return (runs, teeIdx, sb.ToString().TrimEnd(' ', '·'));
    }

    /// <summary>경계 호길이 t의 (X, Y, 보간 Z).</summary>
    private static (double X, double Y, double Z) PointAtParam(IReadOnlyList<Point3> ring, double[] cum, double t)
    {
        int m = cum.Length - 1;
        double tot = cum[m];
        t = ((t % tot) + tot) % tot;
        int lo = 0, hi = m;
        while (lo + 1 < hi) { int md = (lo + hi) / 2; if (cum[md] <= t) lo = md; else hi = md; }
        var a = ring[lo]; var b = ring[(lo + 1) % ring.Count];
        double seg = cum[lo + 1] - cum[lo];
        double u = seg < 1e-12 ? 0 : (t - cum[lo]) / seg;
        return (a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);
    }

    /// <summary>흙(뒷굽) 방향이 경로 진행방향의 왼쪽(+1)인가 — 절토: 흙=계획폴리곤 밖, 성토: 흙=계획폴리곤 안.</summary>
    private static int SoilLeftOf(List<Point3> run, IReadOnlyList<Point3> poly, bool cut)
    {
        if (run.Count < 2) return 1;
        var a = run[0]; var b = run[1];
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return 1;
        double mx = (a.X + b.X) / 2 - dy / len * 0.3, my = (a.Y + b.Y) / 2 + dx / len * 0.3;
        bool leftInside = PointInPolygon(mx, my, poly);
        return cut ? (leftInside ? -1 : 1) : (leftInside ? 1 : -1);
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<Point3> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = poly[i]; var pj = poly[j];
            if ((pi.Y > y) != (pj.Y > y) &&
                x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y + 1e-300) + pi.X)
                inside = !inside;
        }
        return inside;
    }
}
