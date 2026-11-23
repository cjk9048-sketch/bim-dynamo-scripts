using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Buffer;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1123 «이어서는 앞 계획부지를 절대 침범하면 안 돼 — 사면이 늘어져 어쩔 수 없이 침범하면 자동으로 수직옹벽»] <b>앞 부지 보호</b>.
/// <para>«이어서»로 새 구역을 지을 때 앞 구역 계획선 안(평탄면)은 1 mm도 안 바뀌어야 한다. 셋을 한다(순수 — 같은 입력이면 같은 답):</para>
/// <para>① 새 계획선에서 앞 부지(+ 벽 자리 δ)를 뺀다 — 겹쳐 그려도 맞닿게 붙는다(조정자 결정 1123). δ = 맞닿은 곳 높이차 × 옹벽 구배(1:0.01) + 3 cm —
/// TIN은 한 점에 높이가 하나뿐이라 벽 면이 평면 폭을 가져야 하고, 그 폭은 새 부지 쪽에서 낸다.</para>
/// <para>② 새 경계에서 바깥으로 광선을 쏘아 바탕 위에서 절토 · 성토 단 프로파일(단높이 · 구배 · 소단)로 데이라잇까지 걷는다 — 앞 부지 안에 들어가면 그 자리가 «침범».</para>
/// <para>③ 침범 자리를 구간으로 묶어 «0단부터 1:MinSlope · 소단 0» 규칙(수직 옹벽)을 낸다 — 기존 구간 뒤에 붙이면(나중 것이 이긴다) 링 기계가 수직 면을 만든다.
/// 번들 구간 → 옹벽선(WallRuns) → 옹벽 종류별 표준단면 · 터파기 · 수량이 종전 길로 받는다.</para></summary>
public static class PadGuard
{
    /// <summary>결과 — 새 경계 · 자동 옹벽 구간 · 알림 숫자.</summary>
    public sealed class Result
    {
        /// <summary>새 경계(바뀌지 않았으면 들어온 것 그대로 — 같은 객체).</summary>
        public List<Point3> Boundary = new();
        public bool Changed;
        /// <summary>자동 옹벽 구간(절토 · 성토) — 새 경계의 호길이 자.</summary>
        public List<SlopeZone> CutWalls = new(), FillWalls = new();
        /// <summary>계획선과 앞 부지가 겹친 넓이(㎡) — 뺐다.</summary>
        public double OverlapArea;
        /// <summary>벽 자리 폭(m) · 그 셈에 쓴 맞닿은 높이차(m).</summary>
        public double Delta, EdgeRelief;
        /// <summary>빼고 남은 조각 중 버린 것(가장 큰 것만 쓴다).</summary>
        public int DroppedPieces; public double DroppedArea;
        public int Rays, Hits;
        /// <summary>자동 옹벽 구간 길이 합(m).</summary>
        public double WallLen;
        /// <summary>비었으면 진행 · 있으면 멈춘다(사람 글).</summary>
        public string Blocked = "";
        public string Log = "";
        public int WallCount => CutWalls.Count + FillWalls.Count;
    }

    const double RayStep = 0.5;      // 둘레 표본 간격(m)
    const double MarchStep = 0.2;    // 광선 걸음(m)
    const double MaxReach = 400.0;   // 광선 최대 길이(m) — 데이라잇에서 멈춘다
    const double ZoneMargin = 1.0;   // 침범 자리 앞뒤 여유(m)
    const double ZoneGapFill = 2.0;  // 이 미만 틈은 한 구간으로
    const double DeltaPad = 0.03;    // 벽 자리 여유(m)

    /// <summary>★[2차 검토 · 명시 표지] 자동 보호 옹벽 구간의 둘째 규칙 시작단 — 단이 여기까지 오르지 않으므로(MaxBenches 50) 모양에 영향이 없고,
    /// 번들 v7+ 규칙 목록에 그대로 실려(번들 v14 형식 그대로 · 옛 도면 · 옛 판 읽기 그대로) 다시 열어도 «사용자 구간과 같은 모양»과 헷갈리지 않는다.</summary>
    public const int GuardMarkBench = 999;

    /// <summary>자동 보호 옹벽 구간인가(명시 표지) — «0단부터 1:MinSlope · 소단 0» + 표지 줄 · 전체 지정 · 자 없음.</summary>
    public static bool IsGuardZone(SlopeZone z) =>
        z != null && !z.Partial && z.Ref == null && z.Rules.Count == 2 && z.Rules[0].FromBench == 0 && z.Rules[1].FromBench == GuardMarkBench
        && Math.Abs(z.Rules[0].BenchW) < 1e-12 && Math.Abs(z.Rules[1].BenchW) < 1e-12;

    /// <summary>앞 부지 보호를 건다. <paramref name="pads"/> = 앞 구역 계획선(번들 경계) · <paramref name="ground"/> = 이번 바탕(정지면_DH이전).</summary>
    public static Result Apply(IReadOnlyList<Point3> boundary, IReadOnlyList<IReadOnlyList<Point3>> pads, IGroundSurface ground, GradingParams p)
    {
        var res = new Result { Boundary = boundary as List<Point3> ?? new List<Point3>(boundary) };
        var log = new System.Text.StringBuilder();
        var gf = NtsSupport.Factory();
        var bPoly = NtsSupport.ToCleanPolygon(boundary, gf);
        if (bPoly == null || boundary.Count < 3) { res.Log = "계획선을 다각형으로 못 만들었다 — 보호 안 함"; return res; }
        var padPolys = new List<Geometry>();
        foreach (var pd in pads)
        {
            var g = NtsSupport.ToCleanPolygon(pd, gf);
            if (g != null && g.Area > 1e-6) padPolys.Add(g);
        }
        if (padPolys.Count == 0) { res.Log = "앞 부지 없음 — 보호 안 함"; return res; }
        Geometry P = padPolys.Count == 1 ? padPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(padPolys);

        // ① 겹침 + 벽 자리 빼기
        res.OverlapArea = SafeArea(() => bPoly.Intersection(P));
        double relief = EdgeReliefOf(P, bPoly, boundary, ground);
        res.EdgeRelief = relief;
        res.Delta = relief * Math.Max(p.MinSlope, 0) + DeltaPad;
        var bp = new BufferParameters { JoinStyle = JoinStyle.Mitre, MitreLimit = 2.0, QuadrantSegments = 2 };
        Geometry diff;
        try { diff = bPoly.Difference(BufferOp.Buffer(P, res.Delta, bp)); }
        catch (Exception ex) { res.Blocked = "앞 부지와 겹친 부분 못 뺌 → 계획선 확인(" + ex.Message + ")"; return res; }
        var pieces = new List<Polygon>();
        for (int i = 0; i < diff.NumGeometries; i++)
            if (diff.GetGeometryN(i) is Polygon pg && !pg.IsEmpty && pg.Area > 1e-6) pieces.Add(pg);
        if (pieces.Count == 0 || pieces.Max(q => q.Area) < 1.0)
        {
            res.Blocked = "새 계획선이 앞 계획부지 안 → 계획선을 앞 부지 밖으로";
            return res;
        }
        pieces.Sort((a, b) => b.Area.CompareTo(a.Area));
        var keep = pieces[0];
        res.DroppedPieces = pieces.Count - 1;
        res.DroppedArea = pieces.Skip(1).Sum(q => q.Area);
        if (keep.NumInteriorRings > 0)
        {
            res.Blocked = "앞 계획부지가 새 계획선 안에 통째로 → 계획선을 나눠 그리기";
            return res;
        }
        // ★[검토 L3] 떨어진 조각이 크면(남는 것의 20 % 이상) 말없이 버리지 않고 막는다
        if (res.DroppedArea >= keep.Area * 0.2)
        {
            res.Blocked = $"앞 부지가 새 계획선을 둘로 가름(떨어진 조각 {res.DroppedArea:F0} ㎡) → 계획선을 나눠 그리기";
            return res;
        }
        double cut = bPoly.Area - keep.Area;
        if (cut > 1e-4 || res.DroppedPieces > 0)
        {
            res.Changed = true;
            res.Boundary = RingFrom(keep, boundary);
            log.Append($"계획선 고침 — 겹침 {res.OverlapArea:F2}㎡ · 벽 자리 δ {res.Delta * 100:F1}cm(맞닿은 높이차 {relief:F2}m) · 뺀 넓이 {cut:F2}㎡ · 점 {boundary.Count} → {res.Boundary.Count}");
            if (res.DroppedPieces > 0) log.Append($" · 버린 조각 {res.DroppedPieces}개 {res.DroppedArea:F1}㎡");
        }
        else log.Append($"계획선 그대로(앞 부지와 안 겹침 · δ {res.Delta * 100:F1}cm)");

        // ② 광선 — 바탕 위 단 프로파일로 데이라잇까지
        var bnd = res.Boundary;
        int n = bnd.Count;
        var cum = GradingGeometry.CumLen2D(bnd);
        double total = cum[^1];
        bool ccw = SignedA2(bnd) > 0;
        var prepP = PreparedGeometryFactory.Prepare(P);
        var hitC = new List<double>(); var hitF = new List<double>();
        for (int i = 0; i < n; i++)
        {
            var a = bnd[i]; var b = bnd[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, L = Math.Sqrt(ex * ex + ey * ey);
            if (L < 1e-9) continue;
            double ux = ex / L, uy = ey / L;
            double nx = ccw ? uy : -uy, ny = ccw ? -ux : ux;
            int ns = Math.Max(1, (int)Math.Ceiling(L / RayStep));
            for (int k = 0; k <= ns; k++)
            {
                double s = L * k / ns;
                double x = a.X + ux * s, y = a.Y + uy * s, z = a.Z + (b.Z - a.Z) * (s / L);
                Shoot(x, y, z, nx, ny, cum[i] + s);
            }
            // 볼록 꼭짓점(b) 부채 — 다음 변 법선까지
            var c = bnd[(i + 2) % n];
            double fx = c.X - b.X, fy = c.Y - b.Y, M = Math.Sqrt(fx * fx + fy * fy);
            if (M < 1e-9) continue;
            double vx = fx / M, vy = fy / M;
            double cross = ux * vy - uy * vx;
            bool convex = ccw ? cross > 1e-9 : cross < -1e-9;
            if (!convex) continue;
            double mx = ccw ? vy : -vy, my = ccw ? -vx : vx;
            double a0 = Math.Atan2(ny, nx), a1 = Math.Atan2(my, mx);
            double da = a1 - a0; while (da > Math.PI) da -= 2 * Math.PI; while (da < -Math.PI) da += 2 * Math.PI;
            for (int f = 1; f <= 5; f++)
            {
                double an = a0 + da * f / 6.0;
                Shoot(b.X, b.Y, b.Z, Math.Cos(an), Math.Sin(an), cum[i + 1] % total);
            }
        }

        void Shoot(double x0, double y0, double z0, double dx, double dy, double t)
        {
            res.Rays++;
            for (double d = MarchStep; d <= MaxReach; d += MarchStep)
            {
                double x = x0 + dx * d, y = y0 + dy * d;
                if (!ground.TryGetElevation(x, y, out double g)) return;
                bool cutOn = g > z0 + Rise(p, true, d) + 1e-3;
                bool fillOn = g < z0 - Rise(p, false, d) - 1e-3;
                if (!cutOn && !fillOn) return;   // 데이라잇
                if (prepP.Contains(gf.CreatePoint(new Coordinate(x, y))))
                {
                    res.Hits++;
                    (cutOn ? hitC : hitF).Add(t);
                    return;
                }
            }
        }

        // ③ 구간
        res.CutWalls = ZonesOf(hitC, total, p.MinSlope, out double lc);
        res.FillWalls = ZonesOf(hitF, total, p.MinSlope, out double lf);
        res.WallLen = lc + lf;
        log.Append($" · 광선 {res.Rays} · 침범 {res.Hits}(절토 {hitC.Count} · 성토 {hitF.Count}) → 옹벽 구간 절토 {res.CutWalls.Count} · 성토 {res.FillWalls.Count} · 길이 {res.WallLen:F1}m");
        foreach (var (nm, zs) in new[] { ("절토", res.CutWalls), ("성토", res.FillWalls) })
            foreach (var zz in zs) log.Append($"\n  {nm} 옹벽 [{zz.T0:F2}..{zz.T1:F2}] / 둘레 {total:F2}");
        res.Log = log.ToString();
        return res;
    }

    /// <summary>★★[조정자 1123 «자동 옹벽은 한 단 · 전체 높이로 기록» — WallRunBuilder는 안 건드림(사람이 만든 소단 0 옹벽 그대로)]
    /// <b>자동 옹벽 구간의 옹벽선(WallRuns)을 한 단 · 실제 높이로 고쳐 쓴다.</b>
    /// <para>링은 방향 전체 단높이(5 m)마다 표고가 하나라 구간마다 단높이를 못 바꾼다(SlopeZone 설명). 그래서 소단 0 자동 옹벽도 링 짝마다 5 m 벽 여러 개로 잡혀
    /// 종단 글씨 · 막대가 첫 단 5 m(104/109)로 적혔다(실제 6 m — 키트 gm1123f). 자동 구간 안 <b>점만</b> 고른다:
    /// ①2단 이상은 자동 구간 안 점을 빼고(구간 밖 조각은 그대로 남긴다 — 사용자 옹벽과 이어진 옹벽선 · 검토 L6)
    /// ②1단은 자동 구간 안 점의 부지 반대쪽 끝(절토 = 위 · 성토 = 아래)을 바탕(앞 정지면) 높이로 · XY는 1단 면(1:MinSlope 한 평면 · 소단 0)을 높이 비율만큼 늘인 자리로 ③높이 = 점 높이의 최댓값.</para>
    /// <para>자동 구간 판정 = 부지 쪽 점(절토 = 아래 · 성토 = 위)의 호길이가 자동 구간 안.</para></summary>
    public static List<WallRun>? SingleTierRuns(List<WallRun>? runs, IReadOnlyList<Point3> boundary, IReadOnlyList<SlopeZone> guardZones,
        Func<double, double, double?> baseZ, out string log)
    {
        log = "";
        if (runs == null || runs.Count == 0 || guardZones.Count == 0 || boundary.Count < 3) return runs;
        var cum = GradingGeometry.CumLen2D(boundary);
        bool InGuard(Point3 q)
        {
            double t = GradingGeometry.ParamAt(boundary, cum, q.X, q.Y);
            foreach (var z in guardZones) if (z.Contains(t)) return true;
            return false;
        }
        var outp = new List<WallRun>(runs.Count);
        int dropped = 0, fixedN = 0, noBase = 0, kept = 0; double hMaxAll = 0;
        foreach (var r in runs)
        {
            int n = Math.Min(r.Toe.Count, r.Crest.Count);
            var padSide = r.Up ? r.Toe : r.Crest;
            var inG = new bool[n];
            int nIn = 0;
            for (int i = 0; i < n; i++) if (inG[i] = InGuard(padSide[i])) nIn++;
            if (nIn == 0) { outp.Add(r); continue; }
            if (r.Bench >= 1)
            {
                // 자동 구간 밖 조각만 남긴다(0.5 m 미만 조각은 벽으로 못 세움 — WallRunBuilder와 같은 문턱)
                dropped++;
                int s = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool outside = i < n && !inG[i];
                    if (outside && s < 0) s = i;
                    if (!outside && s >= 0)
                    {
                        var to = r.Toe.GetRange(s, i - s); var cr = r.Crest.GetRange(s, i - s);
                        double len = 0; for (int k = 0; k + 1 < cr.Count; k++) len += Math.Sqrt((cr[k + 1].X - cr[k].X) * (cr[k + 1].X - cr[k].X) + (cr[k + 1].Y - cr[k].Y) * (cr[k + 1].Y - cr[k].Y));
                        if (cr.Count >= 2 && len >= 0.5) { outp.Add(new WallRun { Up = r.Up, Bench = r.Bench, Toe = to, Crest = cr, Height = r.Height }); kept++; }
                        s = -1;
                    }
                }
                continue;
            }
            var toe = new List<Point3>(r.Toe); var crest = new List<Point3>(r.Crest);
            double hMax = 0;
            for (int i = 0; i < n; i++)
            {
                if (inG[i])
                {
                    if (r.Up)
                    {
                        var c = crest[i]; var t0 = toe[i]; double? z = baseZ(c.X, c.Y); double h0 = c.Z - t0.Z;
                        if (z.HasValue && z.Value > t0.Z && h0 > 1e-6)
                        {
                            double k = Math.Min((z.Value - t0.Z) / h0, 20);
                            crest[i] = new Point3(t0.X + (c.X - t0.X) * k, t0.Y + (c.Y - t0.Y) * k, z.Value);
                        }
                        else noBase++;
                    }
                    else
                    {
                        var t = toe[i]; var c0 = crest[i]; double? z = baseZ(t.X, t.Y); double h0 = c0.Z - t.Z;
                        if (z.HasValue && z.Value < c0.Z && h0 > 1e-6)
                        {
                            double k = Math.Min((c0.Z - z.Value) / h0, 20);
                            toe[i] = new Point3(c0.X + (t.X - c0.X) * k, c0.Y + (t.Y - c0.Y) * k, z.Value);
                        }
                        else noBase++;
                    }
                }
                hMax = Math.Max(hMax, crest[i].Z - toe[i].Z);
            }
            fixedN++;
            hMaxAll = Math.Max(hMaxAll, hMax);
            outp.Add(new WallRun { Up = r.Up, Bench = 0, Toe = toe, Crest = crest, Height = hMax > 0.1 ? hMax : r.Height });
        }
        log = $"자동 옹벽선 한 단으로 — 고침 {fixedN}줄(높이 최대 {hMaxAll:F2} m) · 뺀 위 단 {dropped}줄(구간 밖 조각 {kept}줄은 남김)" + (noBase > 0 ? $" · 바탕 높이 못 쓴 점 {noBase}(지은 값 그대로)" : "");
        return outp;
    }

    /// <summary>★★[조정자 1123 · 키트 gm1123f 2판] <b>번들에서 다시 뜬 옹벽 윗선 · 아랫선(측점 · 종단 막대 · 글씨 재료)도 자동 구간은 한 단 · 실제 높이로.</b>
    /// <para>종단 · 측점은 옹벽선(WallRuns)이 아니라 번들 링을 다시 떠 단마다 선을 뽑는다(NoriCommand.RebuildEdgeLines) — 그래서 옹벽선을 고쳐도 글씨가 104/109였다.
    /// 자동 구간 안 <b>점만</b>: 2단 이상은 그 점을 빼고(구간 밖 조각은 남김 · 검토 L6) · 1단의 부지 반대쪽 선(절토 = 윗선 · 성토 = 아랫선) 점을 번들 옹벽선(<see cref="SingleTierRuns"/>로 고친 정본) 위 가장 가까운 자리로.
    /// 옹벽선이 없으면(옛 번들) 손대지 않는다.</para></summary>
    public static List<(int Bench, bool IsCrest, List<Point3> Pts)> SingleTierWallLines(List<(int Bench, bool IsCrest, List<Point3> Pts)> lines, bool up,
        IReadOnlyList<Point3> boundary, IReadOnlyList<SlopeZone> guardZones, IReadOnlyList<WallRun>? runs)
    {
        if (lines.Count == 0 || guardZones.Count == 0 || runs == null || runs.Count == 0 || boundary.Count < 3) return lines;
        var cum = GradingGeometry.CumLen2D(boundary);
        bool InGuard(Point3 q)
        {
            double t = GradingGeometry.ParamAt(boundary, cum, q.X, q.Y);
            foreach (var z in guardZones) if (z.Contains(t)) return true;
            return false;
        }
        // 부지 반대쪽 정본 선(절토 = 윗선 · 성토 = 아랫선) — 1단 옹벽선만(구간 밖에 남긴 위 단 조각은 정본이 아니다)
        //   ★[2차 검토] 자동 구간 옹벽선만(부지 쪽 점 절반 이상이 자동 구간 안) — 이어진 사용자 옹벽 선에 잘못 붙지 않게
        var farLines = runs.Where(r => r.Bench == 0 && (up ? r.Toe : r.Crest).Count(InGuard) * 2 >= Math.Max(1, (up ? r.Toe : r.Crest).Count))
                           .Select(r => up ? r.Crest : r.Toe).Where(l => l != null && l.Count >= 2).ToList();
        if (farLines.Count == 0) return lines;
        Point3 Snap(Point3 q)
        {
            double bd = double.MaxValue; Point3 best = q;
            foreach (var l in farLines)
                for (int i = 0; i + 1 < l.Count; i++)
                {
                    var a = l[i]; var b = l[i + 1];
                    double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                    double u = l2 < 1e-18 ? 0 : ((q.X - a.X) * ex + (q.Y - a.Y) * ey) / l2;
                    u = u < 0 ? 0 : (u > 1 ? 1 : u);
                    double px = a.X + ex * u, py = a.Y + ey * u, d = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
                    if (d < bd) { bd = d; best = new Point3(px, py, a.Z + (b.Z - a.Z) * u); }
                }
            return bd <= 1.0 ? best : q;
        }
        var outp = new List<(int, bool, List<Point3>)>(lines.Count);
        foreach (var (bench, isCrest, pts) in lines)
        {
            if (pts == null || pts.Count == 0) { outp.Add((bench, isCrest, pts!)); continue; }
            var inG = pts.Select(InGuard).ToArray();
            if (!inG.Any(v => v)) { outp.Add((bench, isCrest, pts)); continue; }
            if (bench >= 1)
            {
                int s = -1;
                for (int i = 0; i <= pts.Count; i++)
                {
                    bool outside = i < pts.Count && !inG[i];
                    if (outside && s < 0) s = i;
                    if (!outside && s >= 0) { if (i - s >= 2) outp.Add((bench, isCrest, pts.GetRange(s, i - s))); s = -1; }
                }
                continue;
            }
            bool farSide = up ? isCrest : !isCrest;
            outp.Add((bench, isCrest, farSide ? pts.Select((q, i) => inG[i] ? Snap(q) : q).ToList() : pts));
        }
        return outp;
    }

    /// <summary>클립링 다듬기 결과 — 새 링(null = 그대로 써도 됨) · 실패(보호 모드에서 멈춰야 함) · 다듬기 전 클립 ∩ 앞 부지 넓이와 조각 수.</summary>
    public sealed class ClipTrim
    {
        public List<Point3>? Ring;
        public bool Fail;
        /// <summary>멈춤 까닭 — 1 계산 실패 · 2 너무 작음(사면이 앞 부지에 크게 걸림) · 3 둘러쌈(구멍).</summary>
        public int Why;
        public string Log = "";
        /// <summary>다듬기 전 클립 ∩ 앞 부지 넓이(㎡) · 0.005 ㎡ 넘는 조각 수 — 가시(수 c㎡)보다 크면 광선이 놓친 침범(옹벽 기록 없는 단차).</summary>
        public double InPad; public int InPadPieces;
    }

    /// <summary>★[1123 키트 gm1123a 실측] <b>붙여넣을 범위(클립링)를 앞 부지에서 떼어 낸다.</b>
    /// <para>옹벽 구간 끝(부지 모서리)에서 교선이 수직 면을 타고 꼭대기 링까지 올라갔다 내려오는 «가시»(폭 2.8 cm · 길이 15 cm · 높이 125 m)를 냈고,
    /// 그 가시가 앞 부지 모서리 안으로 들어가 붙여넣기가 앞 부지를 12 m 올렸다(바깥 둘레에도 바늘).</para>
    /// <para>① 열기(−<paramref name="spur"/> → +<paramref name="spur"/>) — 폭 2·spur 미만 가시를 없앤다 ② 계획선(B')을 다시 합친다(부지 모서리가 열기로 둥글어지지 않게)
    /// ③ 앞 부지 + <paramref name="gap"/>를 뺀다 — 붙여넣는 면이 앞 부지에 절대 안 닿는다.</para>
    /// <para>★[검토 M2] 클립이 앞 부지(+ gap + spur)에 안 닿으면 손대지 않는다(Ring = null · 종전 결과 그대로).
    /// ★[검토 H2 ②] 다듬다 터지거나 · 남은 것이 절반 미만이거나 · 앞 부지를 둘러싸 구멍이 생기면 Fail — 부르는 쪽이 멈춘다(보호 없는 원 클립으로 물러나지 않는다).</para></summary>
    public static ClipTrim TrimClip(IReadOnlyList<Point3> clip, IReadOnlyList<Point3> plan, IReadOnlyList<IReadOnlyList<Point3>> pads,
        double spur = 0.03, double gap = 0.02)
    {
        var res = new ClipTrim();
        var gf = NtsSupport.Factory();
        var c = NtsSupport.ToCleanPolygon(clip, gf);
        var padPolys = pads.Select(pd => NtsSupport.ToCleanPolygon(pd, gf)).Where(g => g != null).Cast<Geometry>().ToList();
        if (padPolys.Count == 0) { res.Log = "앞 부지 없음 — 그대로"; return res; }
        if (c == null) { res.Fail = true; res.Why = 1; res.Log = "클립링을 다각형으로 못 만들었다"; return res; }
        Geometry P = padPolys.Count == 1 ? padPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(padPolys);
        try { if (!c.Intersects(P.Buffer(gap + spur))) { res.Log = "앞 부지에서 멂 — 그대로"; return res; } }
        catch (Exception ex) { res.Fail = true; res.Why = 1; res.Log = "앞 부지와 견주기 실패(" + ex.Message + ")"; return res; }
        try
        {
            var ip = c.Intersection(P);
            res.InPad = ip.Area;
            for (int i = 0; i < ip.NumGeometries; i++) if (ip.GetGeometryN(i).Area > 0.005) res.InPadPieces++;
        }
        catch { }
        Geometry g;
        try
        {
            g = c.Buffer(-spur).Buffer(spur);
            var pl = NtsSupport.ToCleanPolygon(plan, gf);
            if (pl != null) g = g.Union(pl);
            g = g.Difference(P.Buffer(gap));
        }
        catch (Exception ex) { res.Fail = true; res.Why = 1; res.Log = "클립링 다듬기 실패(" + ex.Message + ")"; return res; }
        var best = NtsSupport.LargestPolygon(g);
        if (best == null || best.Area < c.Area * 0.5) { res.Fail = true; res.Why = 2; res.Log = $"다듬은 클립링이 너무 작다({best?.Area ?? 0:F1} / {c.Area:F1}㎡)"; return res; }
        if (best.NumInteriorRings > 0) { res.Fail = true; res.Why = 3; res.Log = $"사면이 앞 부지를 둘러쌈(구멍 {best.NumInteriorRings}개)"; return res; }
        double removed = c.Area - best.Area;
        int pieces = g.NumGeometries;
        var r = new List<Point3>();
        var cs = best.ExteriorRing.Coordinates;
        for (int i = 0; i + 1 < cs.Length; i++)
        {
            if (r.Count > 0 && Math.Abs(r[^1].X - cs[i].X) < 1e-6 && Math.Abs(r[^1].Y - cs[i].Y) < 1e-6) continue;
            r.Add(new Point3(cs[i].X, cs[i].Y, NearestZ(clip, cs[i].X, cs[i].Y)));
        }
        if ((SignedA2(r) > 0) != (SignedA2(clip) > 0)) r.Reverse();
        res.Ring = r;
        res.Log = $"클립링 다듬음 — 앞 부지에 들어간 넓이 {res.InPad * 1e4:F0}c㎡(조각 {res.InPadPieces}) · 넓이 {c.Area:F2} → {best.Area:F2}㎡(뺀 {removed:F3}㎡) · 점 {clip.Count} → {r.Count}"
            + (pieces > 1 ? $" · 떨어진 조각 {pieces - 1}개 버림" : "");
        return res;
    }

    /// <summary>★[검토 H2 ①] 가상 절토 · 성토면에 넣을 Hide 경계 — 앞 부지 + <paramref name="gap"/>(합집합의 조각마다 바깥 둘레). 클립 모양과 상관없이 붙여넣는 면이 앞 부지를 못 덮는다.</summary>
    public static List<List<Point3>> HideRings(IReadOnlyList<IReadOnlyList<Point3>> pads, double gap = 0.02)
    {
        var outp = new List<List<Point3>>();
        var gf = NtsSupport.Factory();
        var padPolys = pads.Select(pd => NtsSupport.ToCleanPolygon(pd, gf)).Where(g => g != null).Cast<Geometry>().ToList();
        if (padPolys.Count == 0) return outp;
        Geometry P = padPolys.Count == 1 ? padPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(padPolys);
        var B = P.Buffer(gap);   // ★[2차 검토] TrimClip이 빼는 띠와 같은 둥근 띠(하나로)
        for (int i = 0; i < B.NumGeometries; i++)
            if (B.GetGeometryN(i) is Polygon pg && !pg.IsEmpty)
            {
                var r = new List<Point3>();
                var cs = pg.ExteriorRing.Coordinates;
                double z = pads[0].Count > 0 ? pads[0][0].Z : 0;
                for (int k = 0; k + 1 < cs.Length; k++) r.Add(new Point3(cs[k].X, cs[k].Y, z));
                if (r.Count >= 3) outp.Add(r);
            }
        return outp;
    }

    /// <summary>★[검토 L7] 주입한 클립링들과 앞 부지가 겹친 넓이(㎡) — 표본 검사(25 cm)가 놓치는 수 cm 가시까지 직접 잰다. 0이어야 한다.</summary>
    public static double ClipPadOverlap(IEnumerable<IReadOnlyList<Point3>> clips, IReadOnlyList<IReadOnlyList<Point3>> pads)
    {
        var gf = NtsSupport.Factory();
        var padPolys = pads.Select(pd => NtsSupport.ToCleanPolygon(pd, gf)).Where(g => g != null).Cast<Geometry>().ToList();
        if (padPolys.Count == 0) return 0;
        Geometry P = padPolys.Count == 1 ? padPolys[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(padPolys);
        double a = 0;
        foreach (var cl in clips)
        {
            var g = NtsSupport.ToCleanGeometry(cl, gf);
            if (g == null) continue;
            try { a += g.Intersection(P).Area; } catch { a += double.PositiveInfinity; }
        }
        return a;
    }

    /// <summary>검사 표본 — 앞 부지 안 격자(간격 <paramref name="step"/> · 많으면 늘림) + 둘레에서 <paramref name="inset"/>만큼 안쪽 줄(1 m 간격).
    /// 정지면 생성이 합성 뒤에 «앞 부지 안 높이가 바뀌었나»를 잰다(시험 probe도 같은 점을 쓴다).</summary>
    public static List<(double X, double Y)> SamplePoints(IReadOnlyList<Point3> pad, double step = 1.0, double inset = 0.02, int maxGrid = 4000)
    {
        var pts = new List<(double, double)>();
        var gf = NtsSupport.Factory();
        var pg = NtsSupport.ToCleanPolygon(pad, gf);
        if (pg == null) return pts;
        var prep = PreparedGeometryFactory.Prepare(pg);
        var env = pg.EnvelopeInternal;
        double area = Math.Max(pg.Area, 1e-6);
        if (area / (step * step) > maxGrid) step = Math.Sqrt(area / maxGrid);
        for (double x = env.MinX + step * 0.5; x < env.MaxX; x += step)
            for (double y = env.MinY + step * 0.5; y < env.MaxY; y += step)
                if (prep.Contains(gf.CreatePoint(new Coordinate(x, y)))) pts.Add((x, y));
        // 둘레 안쪽 줄 — 안쪽 = 다각형 안에 드는 쪽
        var cs = pg.ExteriorRing.Coordinates;
        for (int i = 0; i + 1 < cs.Length; i++)
        {
            double ex = cs[i + 1].X - cs[i].X, ey = cs[i + 1].Y - cs[i].Y, L = Math.Sqrt(ex * ex + ey * ey);
            if (L < 1e-6) continue;
            double nx = -ey / L, ny = ex / L;
            // ★[1123 gm1123a] 1 m 간격은 모서리 15 cm 가시를 놓쳤다 — 25 cm 간격 · 모서리 바로 곁(inset)까지
            int ns = Math.Max(1, (int)Math.Ceiling(L / 0.25));
            for (int k = 0; k <= ns; k++)
            {
                double s = inset + (L - 2 * inset) * k / ns;
                if (L <= 2 * inset) break;
                double x = cs[i].X + ex / L * s, y = cs[i].Y + ey / L * s;
                foreach (double sg in new[] { 1.0, -1.0 })
                {
                    double qx = x + nx * inset * sg, qy = y + ny * inset * sg;
                    if (prep.Contains(gf.CreatePoint(new Coordinate(qx, qy)))) { pts.Add((qx, qy)); break; }
                }
            }
        }
        return pts;
    }

    /// <summary>바탕 위 단 프로파일 — 경계에서 d만큼 나갔을 때 오른(내린) 높이(단높이 · 구배 · 소단).</summary>
    internal static double Rise(GradingParams p, bool up, double d)
    {
        double slope = Math.Max(up ? p.CutSlope : p.FillSlope, Math.Max(p.MinSlope, 1e-3));
        double w = Math.Max(p.BenchWidthOf(up), 0);
        double rise = 0, run = 0;
        for (int k = 0; k < 400; k++)
        {
            double h = Math.Max(p.BenchHeightAt(up, k), 0.1);
            double face = h * slope;
            if (d <= run + face) return rise + (d - run) / slope;
            run += face; rise += h;
            if (d <= run + w) return rise;
            run += w;
        }
        return rise;
    }

    /// <summary>맞닿은 곳 높이차 — 앞 부지 둘레 점 중 새 계획선에서 1 m 안인 것의 |바탕 − 새 계획고| 최대.</summary>
    static double EdgeReliefOf(Geometry P, Polygon bPoly, IReadOnlyList<Point3> boundary, IGroundSurface ground)
    {
        double best = 0;
        var gf = bPoly.Factory;
        for (int gi = 0; gi < P.NumGeometries; gi++)
        {
            if (P.GetGeometryN(gi) is not Polygon pg) continue;
            var cs = pg.ExteriorRing.Coordinates;
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                double ex = cs[i + 1].X - cs[i].X, ey = cs[i + 1].Y - cs[i].Y, L = Math.Sqrt(ex * ex + ey * ey);
                int ns = Math.Max(1, (int)Math.Ceiling(L / RayStep));
                for (int k = 0; k <= ns; k++)
                {
                    double x = cs[i].X + ex * k / ns, y = cs[i].Y + ey * k / ns;
                    if (bPoly.Distance(gf.CreatePoint(new Coordinate(x, y))) > 1.0) continue;
                    if (!ground.TryGetElevation(x, y, out double g)) continue;
                    double zb = NearestZ(boundary, x, y);
                    best = Math.Max(best, Math.Abs(g - zb));
                }
            }
        }
        return best;
    }

    /// <summary>원 계획선에서 가장 가까운 점의 높이(변 위 보간).</summary>
    internal static double NearestZ(IReadOnlyList<Point3> ring, double x, double y)
    {
        int n = ring.Count; double best = double.MaxValue, z = ring[0].Z;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            double u = l2 < 1e-18 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            double px = a.X + ex * u, py = a.Y + ey * u, d2 = (x - px) * (x - px) + (y - py) * (y - py);
            if (d2 < best) { best = d2; z = a.Z + (b.Z - a.Z) * u; }
        }
        return z;
    }

    /// <summary>NTS 다각형 → 경계 점열(닫는 점 없음 · 원래 도는 방향).
    /// <para>★[검토 H1] 원 정점과 같은 자리(1.5 mm 안)면 <b>원 정점을 그대로</b> 쓴다 — 같은 XY에 Z가 둘(수직 단차)이면 둘 다 원래 차례로 되살린다
    /// (ToCleanPolygon이 XY 중복점을 하나로 합쳐 단차 다음 변이 경사로가 되던 것 · BoundaryReader «단차 소멸 방지»와 같은 함정). 1 mm 격자로 눌린 원 정점도 제자리로.</para>
    /// <para>★[검토 M3] 새 꼭짓점 Z — 원 계획선의 변 위(1.5 mm 안)면 그 변 보간 · 아니면 원 계획선이 한 평면(어긋남 1 mm 안)이면 그 평면 · 그도 아니면 가장 가까운 변.</para></summary>
    static List<Point3> RingFrom(Polygon pg, IReadOnlyList<Point3> orig)
    {
        var cs = pg.ExteriorRing.Coordinates;
        var xy = new List<(double X, double Y)>(cs.Length);
        for (int i = 0; i + 1 < cs.Length; i++)
        {
            var c = cs[i];
            if (xy.Count > 0 && Math.Abs(xy[^1].X - c.X) < 1e-6 && Math.Abs(xy[^1].Y - c.Y) < 1e-6) continue;
            xy.Add((c.X, c.Y));
        }
        double a2 = 0;
        for (int i = 0; i < xy.Count; i++) { var u = xy[i]; var v = xy[(i + 1) % xy.Count]; a2 += u.X * v.Y - v.X * u.Y; }
        if ((a2 > 0) != (SignedA2(orig) > 0)) xy.Reverse();

        int n0 = orig.Count;
        var plane = FitPlane(orig, out double planeResid);
        bool planar = planeResid <= 1e-3;
        const double snap2 = 1.5e-3 * 1.5e-3;
        var r = new List<Point3>(xy.Count + 4);
        void Add(Point3 q) { if (r.Count == 0 || Math.Abs(r[^1].X - q.X) > 1e-9 || Math.Abs(r[^1].Y - q.Y) > 1e-9 || Math.Abs(r[^1].Z - q.Z) > 1e-9) r.Add(q); }
        foreach (var (x, y) in xy)
        {
            var idx = new List<int>();
            for (int j = 0; j < n0; j++) { double dx = orig[j].X - x, dy = orig[j].Y - y; if (dx * dx + dy * dy <= snap2) idx.Add(j); }
            if (idx.Count > 0)
            {
                // 원래 차례 — 0을 지나 이어지면(n−1, 0) 큰 쪽부터
                idx.Sort();
                int start = 0, bestGap = -1;
                for (int k = 0; k < idx.Count; k++)
                {
                    int gap = (idx[(k + 1) % idx.Count] - idx[k] + n0) % n0;
                    if (idx.Count == 1) gap = n0;
                    if (gap > bestGap) { bestGap = gap; start = (k + 1) % idx.Count; }
                }
                for (int k = 0; k < idx.Count; k++) Add(orig[idx[(start + k) % idx.Count]]);
                continue;
            }
            var (d2, zEdge) = NearestOnRing(orig, x, y);
            double z = d2 <= snap2 ? zEdge : planar ? plane.A * x + plane.B * y + plane.C : zEdge;
            Add(new Point3(x, y, z));
        }
        if (r.Count > 1 && Math.Abs(r[0].X - r[^1].X) < 1e-9 && Math.Abs(r[0].Y - r[^1].Y) < 1e-9 && Math.Abs(r[0].Z - r[^1].Z) < 1e-9) r.RemoveAt(r.Count - 1);

        // ★[검토 H1 · 키트 gm1123g] 잘려 나간 변 위의 단차 — 원 단차 쌍(같은 XY · 다른 Z)이 새 변으로 바뀌어 없어졌으면, 그 XY를 가장 가까운 새 변에 비춰 두 점을 원래 차례로 다시 넣는다
        //   (안 넣으면 그 새 변이 104 → 106 경사로가 되어 평탄면 안이 단차 대신 비탈이 된다 — 1판 시험에서 z(45,40) 105.0)
        for (int j = 0; j < n0; j++)
        {
            var s0 = orig[j]; var s1 = orig[(j + 1) % n0];
            if (Math.Abs(s0.X - s1.X) > 1e-6 || Math.Abs(s0.Y - s1.Y) > 1e-6 || Math.Abs(s0.Z - s1.Z) < 1e-6) continue;
            bool kept = r.Any(q => (q.X - s0.X) * (q.X - s0.X) + (q.Y - s0.Y) * (q.Y - s0.Y) <= snap2);
            if (kept) continue;
            int bi = -1; double bd = double.MaxValue, bu = 0;
            for (int i = 0; i < r.Count; i++)
            {
                var a = r[i]; var b = r[(i + 1) % r.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                if (l2 < 1e-18) continue;
                double u = ((s0.X - a.X) * ex + (s0.Y - a.Y) * ey) / l2;
                if (u <= 1e-6 || u >= 1 - 1e-6) continue;   // 변 안쪽에 비칠 때만(꼭짓점 자리면 넣을 곳이 모호)
                double px = a.X + ex * u, py = a.Y + ey * u, d = (s0.X - px) * (s0.X - px) + (s0.Y - py) * (s0.Y - py);
                if (d < bd) { bd = d; bi = i; bu = u; }
            }
            if (bi < 0 || bd > 10.0 * 10.0) continue;   // 10 m 넘게 떨어진 단차(버린 조각 쪽)는 안 옮긴다
            var ra = r[bi]; var rb = r[(bi + 1) % r.Count];
            double qx = ra.X + (rb.X - ra.X) * bu, qy = ra.Y + (rb.Y - ra.Y) * bu;
            // 차례 = 원 차례 · 새 변이 원 단차로 들어오던 변과 반대로 가면 뒤집는다(도는 방향은 맞춰 뒀지만 변 하나는 따로 본다)
            var pv = orig[(j - 1 + n0) % n0];
            double ox = s0.X - pv.X, oy = s0.Y - pv.Y, rx = rb.X - ra.X, ry = rb.Y - ra.Y;
            bool flip = ox * rx + oy * ry < 0;
            r.Insert(bi + 1, new Point3(qx, qy, flip ? s1.Z : s0.Z));
            r.Insert(bi + 2, new Point3(qx, qy, flip ? s0.Z : s1.Z));
        }
        return r;
    }

    /// <summary>원 계획선에서 가장 가까운 변 위 점 — (거리², 보간 Z).</summary>
    static (double D2, double Z) NearestOnRing(IReadOnlyList<Point3> ring, double x, double y)
    {
        int n = ring.Count; double best = double.MaxValue, z = ring[0].Z;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            if (l2 < 1e-18) continue;   // 수직 단차 변(같은 XY)은 건너뛴다 — 높이를 하나로 못 정한다
            double u = ((x - a.X) * ex + (y - a.Y) * ey) / l2;
            u = u < 0 ? 0 : (u > 1 ? 1 : u);
            double px = a.X + ex * u, py = a.Y + ey * u, d2 = (x - px) * (x - px) + (y - py) * (y - py);
            if (d2 < best) { best = d2; z = a.Z + (b.Z - a.Z) * u; }
        }
        return (best, z);
    }

    /// <summary>최소제곱 평면 z = A x + B y + C · 가장 큰 어긋남(m). 점이 셋 미만이거나 풀 수 없으면 어긋남 = 무한.</summary>
    static ((double A, double B, double C) P, double Resid) FitPlaneCore(IReadOnlyList<Point3> pts)
    {
        int n = pts.Count;
        if (n < 3) return ((0, 0, n > 0 ? pts[0].Z : 0), double.PositiveInfinity);
        double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y), mz = pts.Average(p => p.Z);
        double sxx = 0, sxy = 0, syy = 0, sxz = 0, syz = 0;
        foreach (var p in pts) { double dx = p.X - mx, dy = p.Y - my, dz = p.Z - mz; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; sxz += dx * dz; syz += dy * dz; }
        double det = sxx * syy - sxy * sxy;
        if (Math.Abs(det) < 1e-12) return ((0, 0, mz), double.PositiveInfinity);
        double A = (sxz * syy - syz * sxy) / det, B = (syz * sxx - sxz * sxy) / det, C = mz - A * mx - B * my;
        double res = 0; foreach (var p in pts) res = Math.Max(res, Math.Abs(A * p.X + B * p.Y + C - p.Z));
        return ((A, B, C), res);
    }

    static (double A, double B, double C) FitPlane(IReadOnlyList<Point3> pts, out double resid)
    {
        var (p, r) = FitPlaneCore(pts);
        resid = r;
        return p;
    }

    static double SignedA2(IReadOnlyList<Point3> r)
    {
        double a = 0; int n = r.Count;
        for (int i = 0; i < n; i++) { var u = r[i]; var v = r[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
        return a;
    }

    static double SafeArea(Func<Geometry> f) { try { return f().Area; } catch { return 0; } }

    /// <summary>침범 자리(호길이) → 구간 — 틈 2 m 미만은 잇고 앞뒤 1 m 여유 · 0을 지나는 것은 잇는다 · 규칙 «0단부터 1:MinSlope · 소단 0».</summary>
    public static List<SlopeZone> ZonesOf(List<double> ts, double total, double minSlope, out double len)
    {
        len = 0;
        var zs = new List<SlopeZone>();
        if (ts.Count == 0 || total <= 1e-6) return zs;
        ts.Sort();
        var iv = new List<(double A, double B)>();
        double a0 = ts[0], b0 = ts[0];
        for (int i = 1; i < ts.Count; i++)
        {
            if (ts[i] - b0 <= ZoneGapFill) b0 = ts[i];
            else { iv.Add((a0, b0)); a0 = b0 = ts[i]; }
        }
        iv.Add((a0, b0));
        // 0을 지나 이어지는 것(처음과 끝이 둘레 너머로 가까움)
        if (iv.Count > 1 && iv[0].A + total - iv[^1].B <= ZoneGapFill)
        {
            iv[0] = (iv[^1].A - total, iv[0].B);
            iv.RemoveAt(iv.Count - 1);
        }
        foreach (var (A, B) in iv)
        {
            double s = A - ZoneMargin, e = B + ZoneMargin;
            if (e - s >= total - 1e-6) { zs.Clear(); zs.Add(Make(0, total)); len = total; return zs; }
            len += e - s;
            double m0 = ((s % total) + total) % total, m1 = ((e % total) + total) % total;
            zs.Add(Make(m0, m1));
        }
        return zs;

        SlopeZone Make(double t0, double t1)
        {
            var z = new SlopeZone { T0 = t0, T1 = t1, Partial = false };
            z.Rules.Add((0, Math.Max(minSlope, 1e-3), 0.0));
            z.Rules.Add((GuardMarkBench, Math.Max(minSlope, 1e-3), 0.0));   // ★[2차 검토 · 명시 표지] 모양이 아니라 이 줄로 자동 구간을 알아본다
            return z;
        }
    }
}
