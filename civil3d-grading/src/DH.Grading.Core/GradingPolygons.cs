using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>
/// INFRAWORKS 내보내기용 면·선 조립(ralplan Phase C·D·E) — 번들 데이터(경계·finalRing·복원 링)에서
/// NTS 연산으로 커버리지 폴리곤을 만든다. 전부 NtsSupport 방어(1mm 스냅+Buffer(0)+최대폴리곤) 경유.
/// 출력 링 Z=0(flatten) — InfraWorks COVERAGE AREA 용도(계획문 4·5·6번).
/// </summary>
public static class GradingPolygons
{
    /// <summary>순 절토/성토 영역 = finalRing − 계획폴리곤(도넛). 폴리곤별 (링 목록, 면적).</summary>
    public static List<(List<IReadOnlyList<Point3>> Rings, double Area)> PureZone(
        IReadOnlyList<Point3> finalRing, IReadOnlyList<Point3> boundary)
    {
        var outp = new List<(List<IReadOnlyList<Point3>>, double)>();
        var donut = Donut(finalRing, boundary);
        if (donut == null) return outp;
        foreach (var f in ToPolygonFeatures(donut)) outp.Add((f.Rings, f.Area));
        return outp;
    }

    /// <summary>사면/소단 띠 폴리곤(계획문 4번) — 연속 링쌍 strip을 도넛으로 클립.
    /// (r[i], r[i+1]) 사이 strip: i 짝수=사면, 홀수=소단. LEVEL=단 번호(1부터), ELEV=쌍 평균 Z.</summary>
    public static List<(List<IReadOnlyList<Point3>> Rings, double Area, string Kind, int Level, double Elev)>
        Strips(IReadOnlyList<IReadOnlyList<Point3>> rings,
        IReadOnlyList<Point3> finalRing, IReadOnlyList<Point3> boundary)
    {
        var outp = new List<(List<IReadOnlyList<Point3>>, double, string, int, double)>();
        var donut = Donut(finalRing, boundary);
        if (donut == null || rings == null || rings.Count < 2) return outp;
        var gf = NtsSupport.Factory();

        for (int i = 0; i + 1 < rings.Count; i++)
        {
            var inner = NtsSupport.ToCleanPolygon(rings[i], gf);
            var outer = NtsSupport.ToCleanPolygon(rings[i + 1], gf);
            if (inner == null || outer == null) continue;
            Geometry strip;
            try { strip = outer.Difference(inner); } catch { continue; }
            if (strip.IsEmpty) continue;
            Geometry clipped;
            try { clipped = strip.Intersection(donut); } catch { continue; }
            if (clipped.IsEmpty) continue;

            string kind = i % 2 == 0 ? "사면" : "소단";
            int level = i / 2 + 1;
            double elev = (AvgZ(rings[i]) + AvgZ(rings[i + 1])) * 0.5;
            foreach (var f in ToPolygonFeatures(clipped))
                outp.Add((f.Rings, f.Area, kind, level, elev));
        }
        return outp;
    }

    /// <summary>계획폴리곤(계획문 5번) — Z=0 링 목록(구멍 없음).</summary>
    public static List<IReadOnlyList<Point3>>? PlanRings(IReadOnlyList<Point3> boundary)
    {
        var pg = NtsSupport.ToCleanPolygon(boundary);
        return pg == null ? null : ToRings(pg);
    }

    /// <summary>finalRing − 계획폴리곤 도넛(NTS Geometry). 실패/비었으면 null —
    /// 구멍 없는 outer를 돌려주면 계획면까지 절/성토 면적에 조용히 포함되므로(리뷰 M-2) 실패는 눈에 보이게 0건 처리.</summary>
    public static Geometry? Donut(IReadOnlyList<Point3> finalRing, IReadOnlyList<Point3> boundary)
    {
        var gf = NtsSupport.Factory();
        var outer = NtsSupport.ToCleanPolygon(finalRing, gf);
        if (outer == null) return null;
        var hole = NtsSupport.ToCleanPolygon(boundary, gf);
        if (hole == null) return null;
        try
        {
            var d = outer.Difference(hole);
            return d.IsEmpty ? null : d;
        }
        catch { return null; }
    }

    // ── 변환 유틸 ──

    /// <summary>Geometry(폴리곤/멀티폴리곤)를 피처별 (링 목록[외곽+구멍], 면적)로 — Z=0.</summary>
    private static List<(List<IReadOnlyList<Point3>> Rings, double Area)> ToPolygonFeatures(Geometry g)
    {
        var outp = new List<(List<IReadOnlyList<Point3>>, double)>();
        for (int i = 0; i < g.NumGeometries; i++)
        {
            if (g.GetGeometryN(i) is not Polygon pg || pg.IsEmpty) continue;
            outp.Add((ToRings(pg), pg.Area));
        }
        return outp;
    }

    private static List<IReadOnlyList<Point3>> ToRings(Polygon pg)
    {
        var rings = new List<IReadOnlyList<Point3>> { ToPoints(pg.ExteriorRing) };
        for (int h = 0; h < pg.NumInteriorRings; h++) rings.Add(ToPoints(pg.GetInteriorRingN(h)));
        return rings;
    }

    private static List<Point3> ToPoints(LineString ls)
    {
        var pts = new List<Point3>(ls.NumPoints);
        foreach (var c in ls.Coordinates) pts.Add(new Point3(c.X, c.Y, 0)); // flatten
        return pts;
    }

    private static double AvgZ(IReadOnlyList<Point3> ring)
    {
        double s = 0; foreach (var p in ring) s += p.Z; return s / System.Math.Max(ring.Count, 1);
    }

    /// <summary>끝점이 snapTol 이내로 맞닿는 폴리선들을 하나로 이어붙인다(각 단의 직선 crest+사선 daylight를
    /// 한 옹벽선으로 조인 — ARRAY용, JACK). snapTol은 소단폭보다 작아야 인접 단끼리 잘못 안 붙는다.</summary>
    public static List<List<Point3>> JoinPolylines(IReadOnlyList<IReadOnlyList<Point3>> lines, double snapTol)
    {
        var pool = new List<List<Point3>>();
        foreach (var l in lines) if (l != null && l.Count >= 2) pool.Add(new List<Point3>(l));
        var result = new List<List<Point3>>();
        double tol2 = snapTol * snapTol;
        bool Near(Point3 a, Point3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= tol2;

        while (pool.Count > 0)
        {
            var chain = pool[0]; pool.RemoveAt(0);
            bool extended = true;
            while (extended)
            {
                extended = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var cand = pool[i];
                    Point3 cs = chain[0], ce = chain[chain.Count - 1], ds = cand[0], de = cand[cand.Count - 1];
                    if (Near(ce, ds)) { chain.AddRange(cand.GetRange(1, cand.Count - 1)); }
                    else if (Near(ce, de)) { cand.Reverse(); chain.AddRange(cand.GetRange(1, cand.Count - 1)); }
                    else if (Near(cs, de)) { var nc = new List<Point3>(cand); nc.AddRange(chain.GetRange(1, chain.Count - 1)); chain = nc; }
                    else if (Near(cs, ds)) { var nc = new List<Point3>(cand); nc.Reverse(); nc.AddRange(chain.GetRange(1, chain.Count - 1)); chain = nc; }
                    else continue;
                    pool.RemoveAt(i); extended = true; break;
                }
            }
            result.Add(chain);
        }
        return result;
    }

    /// <summary>
    /// 절토 옹벽의 '지표면으로 잘린 상단 모서리'(daylight) — finalRing(정지경계) 중 계획폴리곤 경계와
    /// 겹치지 않는(= 지반과 만나는) 구간만 폴리라인으로 추출. Z는 finalRing 그대로(지반 표고).
    /// 절토 옹벽 상단은 지형 따라 사선/곡선으로 잘리므로 이 선이 옹벽 경로에 필요(JACK). 성토는 불필요.
    /// </summary>
    public static List<List<Point3>> DaylightRuns(
        IReadOnlyList<Point3> finalRing, IReadOnlyList<Point3> boundary, double tol = 0.1, double bermWidth = 1.0,
        IReadOnlyList<double>? benchLevels = null)
    {
        var runs = new List<List<Point3>>();
        if (finalRing == null || finalRing.Count < 2 || boundary == null || boundary.Count < 3) return runs;
        int n = finalRing.Count;
        bool closed = Dist2D(finalRing[0], finalRing[n - 1]) < 1e-6;
        int count = closed ? n - 1 : n; // 닫음 중복점 제외한 유효 정점 수
        if (count < 2) return runs;

        var isDay = new bool[count];
        for (int i = 0; i < count; i++)
            isDay[i] = DistToBoundary(finalRing[i], boundary) > tol; // 계획경계에서 떨어짐 = 지반쪽(daylight)

        // 시작 오프셋: 닫힌 링이면 '계획경계 구간'에서 시작해 daylight run이 이음새에서 안 쪼개지게.
        int start = 0;
        if (closed)
        {
            int firstNonDay = -1;
            for (int i = 0; i < count; i++) if (!isDay[i]) { firstNonDay = i; break; }
            if (firstNonDay < 0) { runs.Add(CopyRing(finalRing, count, closeLoop: true)); return runs; } // 전부 daylight
            start = firstNonDay;
        }

        // daylight 정점 run 수집(경계 앵커 없이 순수 daylight 정점만 — 계획경계로 뻗는 훅 방지).
        List<Point3>? cur = null;
        bool prevDay = false;
        int iters = closed ? count + 1 : count;
        for (int s = 0; s < iters; s++)
        {
            int i = closed ? (start + s) % count : s;
            bool day = isDay[i];
            var pt = finalRing[i];
            if (day) { (cur ??= new List<Point3>()).Add(pt); }
            else if (cur != null) { if (cur.Count >= 2) runs.Add(cur); cur = null; }
            prevDay = day;
        }
        if (cur != null && cur.Count >= 2) runs.Add(cur); // 열린 링 끝

        // [단별 분리 — JACK "각 단별로 하나씩"] daylight 선이 여러 단(bench)을 하나로 걸치므로, 단 경계 Z
        //   (crest 표고: 105·110·115…)에서 잘라 각 조각이 한 단 밴드 안에만 있게 한다(경계 Z에서 보간점 삽입).
        var perBench = new List<List<Point3>>();
        foreach (var run in runs) perBench.AddRange(SplitAtLevels(run, benchLevels));

        // [소단(수평) 구간 제거 — JACK 1원칙] 소단은 Z가 일정(수평). 옹벽선이 소단을 만나면 거기서 끝나야
        //   하고 소단 위로 그어지면 안 됨. daylight 선에서 Z가 일정한(소단) 구간을 잘라 사면(Z 변하는)
        //   구간만 남긴다 → 각 단마다 사면 옹벽선 하나. 추가로 헤어핀 짧은 횡단·양끝 꼬리도 정리.
        var cleaned = new List<List<Point3>>();
        foreach (var run in perBench)
            foreach (var pc in SplitDropBermCrossings(run, bermWidth))
            {
                var t = TrimFlatZEnds(pc); // 소단(수평 Z) 꼬리 제거 — 소단 위 선 금지(1원칙)
                if (t.Count >= 2 && Length2D(t) >= System.Math.Max(2.5, bermWidth * 2.5)) cleaned.Add(t);
            }
        return cleaned;
    }

    /// <summary>조각 양끝의 '소단(수평, Z 일정)' 세그먼트 제거 — 사면선이 소단 위로 그어지지 않게(JACK 1원칙).
    /// Z 변화가 거의 없는(|dZ|<0.05m) 끝 세그먼트를 반복 제거해 사면(Z 변하는) 구간만 남긴다.</summary>
    private static List<Point3> TrimFlatZEnds(IReadOnlyList<Point3> line)
    {
        var pc = new List<Point3>(line);
        const double flat = 0.05; // 이보다 작은 Z변화 = 소단(수평)으로 간주
        while (pc.Count >= 3 && System.Math.Abs(pc[1].Z - pc[0].Z) < flat) pc.RemoveAt(0);
        while (pc.Count >= 3 && System.Math.Abs(pc[pc.Count - 1].Z - pc[pc.Count - 2].Z) < flat) pc.RemoveAt(pc.Count - 1);
        return pc;
    }

    /// <summary>선을 단 경계 표고(levels)에서 분할 — 각 조각이 한 단 밴드 안에만 들어가게(경계 Z 보간 삽입).</summary>
    private static List<List<Point3>> SplitAtLevels(IReadOnlyList<Point3> line, IReadOnlyList<double>? levels)
    {
        var pieces = new List<List<Point3>>();
        if (line.Count < 2) { if (line.Count >= 1) pieces.Add(new List<Point3>(line)); return pieces; }
        if (levels == null || levels.Count == 0) { pieces.Add(new List<Point3>(line)); return pieces; }
        int Band(double z) { int b = 0; foreach (var L in levels) if (z >= L - 1e-6) b++; return b; }

        var cur = new List<Point3> { line[0] };
        for (int k = 1; k < line.Count; k++)
        {
            var p0 = line[k - 1]; var p1 = line[k];
            int b0 = Band(p0.Z), b1 = Band(p1.Z);
            if (b0 == b1) { cur.Add(p1); continue; }
            // p0~p1 사이의 각 경계 표고에서 보간점 삽입 후 분할(진행 방향 순으로 정렬)
            var cross = new List<double>();
            foreach (var L in levels) if (L > System.Math.Min(p0.Z, p1.Z) && L < System.Math.Max(p0.Z, p1.Z)) cross.Add(L);
            cross.Sort();
            if (p1.Z < p0.Z) cross.Reverse();
            foreach (var L in cross)
            {
                double t = System.Math.Abs(p1.Z - p0.Z) < 1e-9 ? 0 : (L - p0.Z) / (p1.Z - p0.Z);
                var ip = new Point3(p0.X + t * (p1.X - p0.X), p0.Y + t * (p1.Y - p0.Y), L);
                cur.Add(ip);
                if (cur.Count >= 2) pieces.Add(cur);
                cur = new List<Point3> { ip };
            }
            cur.Add(p1);
        }
        if (cur.Count >= 2) pieces.Add(cur);
        return pieces;
    }

    /// <summary>헤어핀(꺾임>107°)에서 분할 후, 소단폭 규모의 짧은 횡단 조각을 버려 종단 옹벽선만 남긴다.</summary>
    private static List<List<Point3>> SplitDropBermCrossings(IReadOnlyList<Point3> line, double bermWidth)
    {
        var result = new List<List<Point3>>();
        if (line.Count < 2) return result;
        double dropLen = System.Math.Max(2.5, bermWidth * 2.5);
        const double cosThresh = -0.3; // 꺾임 > 약 107° = 소단 건너뜀 반전
        var pieces = new List<List<Point3>>();
        var cur = new List<Point3> { line[0] };
        for (int k = 1; k < line.Count - 1; k++)
        {
            cur.Add(line[k]);
            double v1x = line[k].X - line[k - 1].X, v1y = line[k].Y - line[k - 1].Y;
            double v2x = line[k + 1].X - line[k].X, v2y = line[k + 1].Y - line[k].Y;
            double l1 = System.Math.Sqrt(v1x * v1x + v1y * v1y), l2 = System.Math.Sqrt(v2x * v2x + v2y * v2y);
            if (l1 > 1e-9 && l2 > 1e-9 && (v1x * v2x + v1y * v2y) / (l1 * l2) < cosThresh)
            { pieces.Add(cur); cur = new List<Point3> { line[k] }; } // 헤어핀에서 분할(정점 공유)
        }
        cur.Add(line[line.Count - 1]);
        pieces.Add(cur);
        foreach (var pc in pieces)
        {
            var t = TrimEndStubs(pc, bermWidth); // 조각 양끝의 짧은 횡단 꼬리(훅) 제거
            if (t.Count >= 2 && Length2D(t) >= dropLen) result.Add(t); // 짧은 횡단 조각 버림
        }
        return result;
    }

    /// <summary>조각 양끝의 '짧고(≈소단폭) 급하게 꺾이는' 꼬리 세그먼트 제거 — 소단 건너뜀 잔재(훅) 제거.
    /// 짧아도 직진(collinear) 꼬리는 유지(밀도 정점일 뿐), 꺾이는 꼬리만 잘라낸다(JACK 훅 스샷).</summary>
    private static List<Point3> TrimEndStubs(IReadOnlyList<Point3> line, double bermWidth)
    {
        var pc = new List<Point3>(line);
        double shortLen = bermWidth * 1.2;
        bool Sharp(Point3 a, Point3 b, Point3 c)
        {
            double v1x = b.X - a.X, v1y = b.Y - a.Y, v2x = c.X - b.X, v2y = c.Y - b.Y;
            double l1 = System.Math.Sqrt(v1x * v1x + v1y * v1y), l2 = System.Math.Sqrt(v2x * v2x + v2y * v2y);
            if (l1 < 1e-9 || l2 < 1e-9) return true; // 퇴화 세그 → 제거 대상
            return (v1x * v2x + v1y * v2y) / (l1 * l2) < 0.5; // 꺾임 > 약 60°
        }
        // 앞끝: 첫 세그가 짧고 다음 세그와 급하게 꺾이면 첫 점 제거(반복)
        while (pc.Count >= 3 && Dist2D(pc[0], pc[1]) < shortLen && Sharp(pc[0], pc[1], pc[2]))
            pc.RemoveAt(0);
        // 뒤끝: 마지막 세그가 짧고 직전 세그와 급하게 꺾이면 끝 점 제거(반복)
        while (pc.Count >= 3 && Dist2D(pc[pc.Count - 2], pc[pc.Count - 1]) < shortLen
               && Sharp(pc[pc.Count - 3], pc[pc.Count - 2], pc[pc.Count - 1]))
            pc.RemoveAt(pc.Count - 1);
        return pc;
    }

    private static double Length2D(IReadOnlyList<Point3> pc)
    {
        double s = 0;
        for (int k = 0; k < pc.Count - 1; k++) s += Dist2D(pc[k], pc[k + 1]);
        return s;
    }

    private static List<Point3> CopyRing(IReadOnlyList<Point3> ring, int count, bool closeLoop)
    {
        var r = new List<Point3>(count + 1);
        for (int i = 0; i < count; i++) r.Add(ring[i]);
        if (closeLoop && count > 0) r.Add(ring[0]);
        return r;
    }

    private static double DistToBoundary(Point3 p, IReadOnlyList<Point3> boundary)
    {
        double best = double.MaxValue;
        int n = boundary.Count;
        for (int i = 0; i < n; i++)
        {
            var a = boundary[i]; var b = boundary[(i + 1) % n];
            double vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
            double t = len2 < 1e-12 ? 0 : ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double qx = a.X + t * vx, qy = a.Y + t * vy;
            double d = System.Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
            if (d < best) best = d;
        }
        return best;
    }

    private static double Dist2D(Point3 a, Point3 b)
        => System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
