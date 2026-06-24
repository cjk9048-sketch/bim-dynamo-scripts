namespace DH.Grading.Core;

/// <summary>
/// 평면도에서 잘 보이도록 "정지 경계선"(단 모서리 + daylight 끝선)을 폴리라인 점목록으로 추출한다.
/// 단 모서리 링은 daylight 폐합선 안쪽으로 클립(과확장 제거). 계획 경계는 사용자 폴리선이 이미 있으므로 제외.
/// </summary>
public static class DesignLineExtractor
{
    public static List<List<Point3>> Extract(GradingBreaklineResult result)
    {
        var lines = new List<List<Point3>>();
        var day = result.Breaklines.FirstOrDefault(b => b.Kind == BreaklineKind.Daylight);
        var dayPts = day?.Points;

        // daylight 끝선(toe) — 닫힌 선
        if (dayPts is { Count: >= 3 }) lines.Add(Close(dayPts));

        // 단 모서리(소단·사면 경계) — daylight 폴리곤 안쪽 점만 남겨 run 분할
        foreach (var bl in result.Breaklines)
        {
            if (bl.Kind != BreaklineKind.BenchEdge) continue;
            if (bl.Points.Count < 2) continue;

            if (dayPts is not { Count: >= 3 })
            {
                lines.Add(new List<Point3>(bl.Points));
                continue;
            }
            AddClippedRuns(bl.Points, dayPts, lines);
        }
        return lines;
    }

    private static void AddClippedRuns(List<Point3> pts, List<Point3> poly, List<List<Point3>> outp)
    {
        List<Point3>? run = null;
        foreach (var p in pts)
        {
            if (PolygonGeometry.Contains(poly, p.X, p.Y))
            {
                run ??= new List<Point3>();
                run.Add(p);
            }
            else
            {
                if (run is { Count: >= 2 }) outp.Add(run);
                run = null;
            }
        }
        if (run is { Count: >= 2 }) outp.Add(run);
    }

    private static List<Point3> Close(List<Point3> pts)
    {
        var l = new List<Point3>(pts);
        if (l.Count >= 3) l.Add(l[0]);
        return l;
    }
}
