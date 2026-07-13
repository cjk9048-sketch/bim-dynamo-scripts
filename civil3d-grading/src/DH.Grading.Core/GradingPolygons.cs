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
    /// 한 옹벽선으로 조인 — ARRAY용, JACK). benchLevels가 있으면 **단별로 그룹핑해 같은 단끼리만 조인**한다
    /// (코너에서 인접 단이 XY로 가까워도 병합 안 됨 — 2단·3단 합쳐지던 버그 수정). 근접 폐합 루프는 닫는다.</summary>
    public static List<List<Point3>> JoinPolylines(IReadOnlyList<IReadOnlyList<Point3>> lines, double snapTol,
        IReadOnlyList<double>? benchLevels = null)
    {
        // 단별 그룹핑 — 각 선의 단 = band(maxZ − eps)(crest Z_k와 그 아래 daylight가 같은 단으로 묶임).
        int Band(double z) { int b = 0; if (benchLevels != null) foreach (var L in benchLevels) if (z >= L - 1e-6) b++; return b; }
        int Key(IReadOnlyList<Point3> l) { double mz = double.MinValue; foreach (var p in l) if (p.Z > mz) mz = p.Z; return Band(mz - 0.01); }

        var groups = new Dictionary<int, List<List<Point3>>>();
        foreach (var l in lines)
        {
            if (l == null || l.Count < 2) continue;
            int k = benchLevels == null || benchLevels.Count == 0 ? 0 : Key(l);
            (groups.TryGetValue(k, out var g) ? g : groups[k] = new List<List<Point3>>()).Add(new List<Point3>(l));
        }

        var result = new List<List<Point3>>();
        double tol2 = snapTol * snapTol;
        // 근접 폐합은 '거의 맞닿은' 이음매만(느슨하면 소단 너머 억지 폐합). 정확 끝점 병합 방침에 맞춰 좁게.
        double closeGap = System.Math.Max(0.3, snapTol * 2);
        bool Near(Point3 a, Point3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= tol2;

        foreach (var key in groups.Keys)
        {
            var pool = groups[key];
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
                // [밴드 클램프-분할] 조인 결과가 자기 단 밴드[lo,hi] 밖(소단 노이즈로 dip)으로 삐지면 그 부분을
                //   빼고 in-band 연속 구간만 남긴다(V자 소단횡단 제거). benchLevels 있을 때만.
                foreach (var seg in ClampSplitToBand(chain, benchLevels, key))
                {
                    // 근접 폐합 루프 닫기(1단 둘레 한 바퀴) — 끝-시작 간격이 작고 선이 그 10배 이상 길면 닫는다.
                    double gap = Dist2D(seg[0], seg[seg.Count - 1]);
                    if (gap > 1e-6 && gap < closeGap && Length2D(seg) > gap * 10) seg.Add(seg[0]);
                    result.Add(seg);
                }
            }
        }
        return result;
    }

    /// <summary>조인된 선을 자기 단 밴드[levels[key-1], levels[key]] 밖 점을 빼고 in-band 연속 구간만 반환.
    /// benchLevels 없으면 원본 그대로. 소단 노이즈로 dip한 V자(소단횡단) 구간 제거용.</summary>
    private static List<List<Point3>> ClampSplitToBand(List<Point3> line, IReadOnlyList<double>? levels, int key)
    {
        if (levels == null || levels.Count == 0) return new List<List<Point3>> { line };
        double lo = (key >= 1 && key <= levels.Count ? levels[key - 1] : double.MinValue) - 0.05;
        double hi = (key < levels.Count ? levels[key] : double.MaxValue) + 0.05;
        var outp = new List<List<Point3>>();
        var cur = new List<Point3>();
        foreach (var p in line)
        {
            if (p.Z >= lo && p.Z <= hi) cur.Add(p);
            else { if (cur.Count >= 2) outp.Add(cur); cur = new List<Point3>(); }
        }
        if (cur.Count >= 2) outp.Add(cur);
        return outp.Count > 0 ? outp : new List<List<Point3>> { line };
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

        // [소단(berm) 레벨 구간 제거 — JACK 1원칙] 소단은 단 경계 표고(crest Z). 토우(아래 소단)에 '수평으로 길게
        //   눕는' 구간(진짜 소단 횡단)만 제거해 각 단이 소단 위로 안 넘어가게. ※단, 급하게 내려오며 소단을 찍고
        //   끝나는 정상 꼬리는 유지 → 옹벽선이 소단까지 도달(끝이 소단에 못 미치던 버그 수정, 0710). 바닥(pad=최저
        //   레벨)은 제외(실제 바닥 옹벽면 유지). CSV 실측으로 V자 소단횡단 소거 확인.
        double pad = double.MaxValue;
        if (benchLevels != null) foreach (var L in benchLevels) if (L < pad) pad = L;
        double minLen = System.Math.Max(2.5, bermWidth * 2.5);
        var cleaned = new List<List<Point3>>();
        foreach (var run in perBench)
        {
            var pieces = benchLevels != null && benchLevels.Count > 1
                ? RemoveToeRuns(run, benchLevels, pad, minLen)
                : new List<List<Point3>> { new List<Point3>(run) };
            foreach (var pc in pieces)
            {
                var t = TrimFlatZEnds(pc, bermWidth); // 끝의 짧은 수평 꼬리 추가 정리
                SnapEndsToLevels(t, benchLevels, finalRing); // 끝점을 소단(토우)/crest 레벨에 딱 닿게 연장
                if (t.Count < 2) continue;
                // [바닥 닿는 끝 조각 유지 — 절토-성토 경계 버그 수정, 0713] 절토가 얕아지는 경계에서 최하단
                //   (pad에 닿는) 사선 조각이 minLen(2.5m) 미만이라 통째 버려져 빨간선이 소단에서 끊기던 문제.
                //   → 조각이 바닥(pad+0.5) 근처까지 내려오고(실제 내려오는 면: Z변화≥0.8m) 있으면 minLen을 1m로 완화.
                double tmin = double.MaxValue, tmax = double.MinValue;
                foreach (var p in t) { if (p.Z < tmin) tmin = p.Z; if (p.Z > tmax) tmax = p.Z; }
                double useMin = (benchLevels != null && tmin <= pad + 0.5 && tmax - tmin >= 0.8) ? 1.0 : minLen;
                if (Length2D(t) >= useMin) cleaned.Add(t);
            }
        }
        LastDaylightDiag = $"raw {runs.Count}런({runs.Sum(Length2D):F0}m) → 단분할 {perBench.Count} → 토우제거·정리 {cleaned.Count}조각({cleaned.Sum(Length2D):F0}m)";
        return cleaned;
    }

    /// <summary>조각에서 '아래(토우) 소단 레벨에 수평으로 눕는' 구간만 제거 — 소단 횡단 제거(JACK 1원칙).
    /// 아래 레벨 = minZ 근처 단 경계 표고. 그게 바닥(pad)이면 제거 안 함(바닥 옹벽면 유지). crest쪽(위 레벨)은
    /// 안 건드려 crest선과 이어짐. ※핵심: 토우 밴드(±0.5m)에 들어와도 **수평길이≥minLen 이면서 거의 평탄(Z변화
    /// &lt;0.3m)** 일 때만 '소단 눕기'로 보고 제거한다. 급강하로 소단을 찍고 끝나는 정상 꼬리·짧은 run은 유지 →
    /// 옹벽선 끝이 소단까지 도달(끝이 0.5m 못 미치던 버그 수정, 0710).</summary>
    private static List<List<Point3>> RemoveToeRuns(IReadOnlyList<Point3> line, IReadOnlyList<double> levels, double pad, double minLen)
    {
        if (line.Count < 2) return new List<List<Point3>>();
        double zmin = double.MaxValue; foreach (var p in line) if (p.Z < zmin) zmin = p.Z;
        double lower = double.MinValue;
        foreach (var L in levels) if (L <= zmin + 0.3 && L > lower) lower = L;
        if (lower == double.MinValue || System.Math.Abs(lower - pad) < 1e-6)
            return new List<List<Point3>> { new List<Point3>(line) }; // 바닥 레벨 → 유지

        var result = new List<List<Point3>>();
        bool Near(double z) => System.Math.Abs(z - lower) < 0.5;
        int i = 0;
        var cur = new List<Point3> { line[0] };
        while (i + 1 < line.Count)
        {
            if (Near(line[i].Z) && Near(line[i + 1].Z))
            {
                // 토우 밴드 내 연속 run [i..j] 수집.
                int j = i;
                while (j + 1 < line.Count && Near(line[j + 1].Z)) j++;
                // '소단 눕기' 판정: 수평길이 충분(≥minLen) + 거의 평탄(Z변화<0.3m). 둘 다 만족할 때만 제거.
                //   급강하 꼬리(Z변화 큼)·짧은 run은 유지 → 옹벽선이 소단(토우)까지 도달.
                double hlen = 0, zlo = line[i].Z, zhi = line[i].Z;
                for (int k = i; k < j; k++) hlen += Dist2D(line[k], line[k + 1]);
                for (int k = i; k <= j; k++) { if (line[k].Z < zlo) zlo = line[k].Z; if (line[k].Z > zhi) zhi = line[k].Z; }
                if (hlen >= minLen && zhi - zlo < 0.3)
                { if (cur.Count >= 2) result.Add(cur); cur = new List<Point3> { line[j] }; i = j; } // 소단 눕기 → 제거
                else { cur.Add(line[i + 1]); i++; }                                                  // 정상 꼬리 → 유지
            }
            else { cur.Add(line[i + 1]); i++; }
        }
        if (cur.Count >= 2) result.Add(cur);
        return result;
    }

    /// <summary>선의 양 끝점이 단 레벨(소단=토우 또는 crest)에 가깝게(≤0.6m) 못 미쳐 끝나면, 그 레벨까지 연장한 점을
    /// 끝에 덧붙인다 — 옹벽선 끝이 소단에 딱 닿게(끝이 미세하게 못 미치던 것 보정, 0710/0713). 연장점 선택 우선순위:
    /// ①마지막 선분 방향 선형 연장(XY≤0.35m면 채택, 내부단에서 정확) ②과연장(바닥단: 토우가 경계 따라 완만→수직
    /// 급변, 실측 9.7m 스파이크)이면 원본 finalRing에서 그 레벨(±0.05m)의 실제 토우점을 2m 이내에서 찾아 복원(가장
    /// 정확) ③없으면 tip 바로 아래로 수직 내림(수직 절토벽 가정). 이미 도달(≤1mm)했거나 레벨에서 먼(사면 중간 끊김,
    /// &gt;0.6m) 끝점은 건드리지 않는다. in-place 수정.</summary>
    private static void SnapEndsToLevels(List<Point3> line, IReadOnlyList<double>? levels, IReadOnlyList<Point3>? finalRing = null)
    {
        if (levels == null || levels.Count == 0 || line.Count < 2) return;
        const double maxXYext = 0.35; // 선분방향 연장 스파이크 방지 상한
        const double toeSnap = 2.0;   // 원본 토우점 복원 반경
        double Nearest(double z)
        { double best = z, bd = double.MaxValue; foreach (var L in levels) { double d = System.Math.Abs(L - z); if (d < bd) { bd = d; best = L; } } return best; }
        // finalRing에서 tip 근처(≤2m)·targetZ(±0.05m)인 실제 토우점을 찾는다(없으면 null).
        Point3? RealToe(Point3 tip, double targetZ)
        {
            if (finalRing == null) return null;
            Point3? best = null; double bd = toeSnap * toeSnap;
            foreach (var p in finalRing)
            {
                if (System.Math.Abs(p.Z - targetZ) > 0.05) continue;
                double d = (p.X - tip.X) * (p.X - tip.X) + (p.Y - tip.Y) * (p.Y - tip.Y);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }
        // tip을 targetZ까지 연장한 점. ①마지막 선분 방향 연장(XY≤상한) ②원본 토우점 복원 ③수직 내림(gap≤0.45만).
        //   토우복원이 안 되고 gap이 크면(0.45~0.7) 수직내림은 오배치 위험 → 연장 포기(null).
        Point3? Extend(Point3 tip, Point3 prev, double targetZ)
        {
            double dz = tip.Z - prev.Z;
            if (System.Math.Abs(dz) >= 1e-6)
            {
                double f = (targetZ - prev.Z) / dz; // prev 기준 보간계수(끝 너머면 f>1)
                double nx = prev.X + (tip.X - prev.X) * f, ny = prev.Y + (tip.Y - prev.Y) * f;
                double ext = System.Math.Sqrt((nx - tip.X) * (nx - tip.X) + (ny - tip.Y) * (ny - tip.Y));
                if (ext <= maxXYext) return new Point3(nx, ny, targetZ); // 선분방향 연장
            }
            var toe = RealToe(tip, targetZ);
            if (toe is Point3 tp) return tp;                                   // 원본 토우 복원(정확)
            return System.Math.Abs(targetZ - tip.Z) <= 0.45                    // 수직 내림은 gap 작을 때만
                ? new Point3(tip.X, tip.Y, targetZ) : (Point3?)null;
        }
        // 시작 끝: line[0]을 line[1] 반대로 연장
        double t0 = Nearest(line[0].Z), g0 = System.Math.Abs(t0 - line[0].Z);
        if (g0 > 1e-3 && g0 <= 0.7) { var p = Extend(line[0], line[1], t0); if (p is Point3 pp) line.Insert(0, pp); }
        // 끝 끝: line[n-1]을 line[n-2] 반대로 연장
        int n = line.Count; double t1 = Nearest(line[n - 1].Z), g1 = System.Math.Abs(t1 - line[n - 1].Z);
        if (g1 > 1e-3 && g1 <= 0.7) { var p = Extend(line[n - 1], line[n - 2], t1); if (p is Point3 pp) line.Add(pp); }
    }

    /// <summary>선에서 '짧은 수평(소단)' 구간을 잘라 버리고 사면·긴수평 구간만 조각으로 반환(JACK 1원칙).
    /// 수평 run(|dZ|<0.05)이 소단 규모(≤2.5m)면 소단으로 보고 그 자리에서 분할·제거(위치 무관),
    /// 긴 수평 run은 레벨지형 위 실제 옹벽면이므로 유지. 헤어핀 분할과 달리 긴 면을 파편화하지 않는다.</summary>
    private static List<List<Point3>> RemoveShortFlatBerms(IReadOnlyList<Point3> line, double bermWidth)
    {
        var result = new List<List<Point3>>();
        if (line.Count < 2) return result;
        const double flat = 0.05;
        double maxBerm = System.Math.Max(2.5, bermWidth * 2.5);
        int i = 0;
        var cur = new List<Point3> { line[0] };
        while (i + 1 < line.Count)
        {
            if (System.Math.Abs(line[i + 1].Z - line[i].Z) < flat) // 수평 run 시작
            {
                int j = i; double flen = 0;
                while (j + 1 < line.Count && System.Math.Abs(line[j + 1].Z - line[j].Z) < flat)
                { flen += Dist2D(line[j], line[j + 1]); j++; }
                if (flen < maxBerm)
                { if (cur.Count >= 2) result.Add(cur); cur = new List<Point3> { line[j] }; i = j; } // 짧은 수평=소단 → 분할·제거
                else
                { for (int k = i; k < j; k++) cur.Add(line[k + 1]); i = j; } // 긴 수평=옹벽면 → 유지
            }
            else { cur.Add(line[i + 1]); i++; }
        }
        if (cur.Count >= 2) result.Add(cur);
        return result;
    }

    /// <summary>DaylightRuns 단계별 진단(옹벽선 누락 추적) — DHINFRA 로그에 기록.</summary>
    public static string LastDaylightDiag = "";

    /// <summary>조각 양끝의 '소단(짧은 수평, Z 일정)' 꼬리만 제거 — 사면선이 소단 위로 안 그어지게(JACK 1원칙).
    /// ★핵심: 끝의 수평 run이 '짧을 때만'(≤소단 규모) 통째 제거. 긴 수평은 레벨지형 위 실제 옹벽면이므로 유지
    /// (평평한 면의 옹벽선이 통째 사라지던 버그 수정 — 맨아래 단 한 면 누락). |dZ|<0.05m = 수평으로 간주.</summary>
    private static List<Point3> TrimFlatZEnds(IReadOnlyList<Point3> line, double bermWidth)
    {
        var pc = new List<Point3>(line);
        const double flat = 0.05;
        double maxBerm = System.Math.Max(2.5, bermWidth * 2.5); // 이보다 긴 수평 run = 레벨 옹벽면 → 유지
        // 앞끝 수평 run
        {
            int j = 0; double len = 0;
            while (j + 1 < pc.Count && System.Math.Abs(pc[j + 1].Z - pc[j].Z) < flat) { len += Dist2D(pc[j], pc[j + 1]); j++; }
            if (j >= 1 && j < pc.Count - 1 && len < maxBerm) pc.RemoveRange(0, j); // 짧은 수평(소단)만 통째 제거
        }
        // 뒤끝 수평 run
        {
            int j = pc.Count - 1; double len = 0;
            while (j - 1 >= 0 && System.Math.Abs(pc[j].Z - pc[j - 1].Z) < flat) { len += Dist2D(pc[j - 1], pc[j]); j--; }
            if (j <= pc.Count - 2 && j > 0 && len < maxBerm) pc.RemoveRange(j + 1, pc.Count - 1 - j);
        }
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
