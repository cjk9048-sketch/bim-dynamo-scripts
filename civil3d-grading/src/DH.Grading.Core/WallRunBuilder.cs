using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>
/// [옹벽 재설계 0805 — 옹벽선_재설계.md P2] 단 링에서 **옹벽선(WallRun)을 확정**한다.
/// <para>
/// 정지면을 만드는 그 순간, 지표면을 만든 것과 <b>같은 링</b>에서 뽑는다. 이렇게 확정한 선을 번들 v9에 저장하고
/// 내보내기는 읽기만 하므로, 종전처럼 '내보내기가 링을 다시 계산해 지표면과 어긋나는' 일이 원천적으로 없다.
/// </para>
/// 순수 기하라 Civil3D 없이 하네스로 검증할 수 있다.
/// </summary>
public static class WallRunBuilder
{
    /// <summary>직전 <see cref="Build"/>의 진단 — 조용히 버려지는 자리마다 사유별 계수기.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <summary>
    /// 링 목록에서 이 방향(<paramref name="up"/>)의 옹벽선을 뽑는다.
    /// </summary>
    /// <param name="boundary">계획경계 — 호길이 param 기준.</param>
    /// <param name="rings">GradingGeometry가 만든 단 링(rings[0]=pad). 벽면은 홀수 k: 토우=rings[k-1] · 크레스트=rings[k].</param>
    /// <param name="zones">구간별 구배 규칙(없으면 전역 구배만 본다).</param>
    /// <param name="globalSlope">이 방향의 전역 구배 n.</param>
    /// <param name="minSlope">최소 구배(이하면 '수직=옹벽'). 보통 0.05.</param>
    public static List<WallRun> Build(
        IReadOnlyList<Point3> boundary,
        IReadOnlyList<IReadOnlyList<Point3>> rings,
        IReadOnlyList<SlopeZone>? zones,
        bool up, double globalSlope, double minSlope)
    {
        var outp = new List<WallRun>();
        if (boundary == null || boundary.Count < 3 || rings == null || rings.Count < 2)
        { LastDiag = "경계/링 없음"; return outp; }

        var cum = GradingGeometry.CumLen2D(boundary);
        double zBase = System.Math.Max(globalSlope, minSlope);
        bool globalIsWall = globalSlope <= minSlope + 1e-9;

        // 이 단(bench)이 이 호길이(t)에서 수직(옹벽)인가.
        //   구간이 덮으면 그 구간의 규칙을, 안 덮으면 전역 구배를 따른다
        //   (InfraworksCommand의 zoneKeep과 같은 판정이어야 노리선·SHP와 어긋나지 않는다).
        bool IsWall(double t, int bench)
        {
            if (zones != null)
                foreach (var z in zones)
                    if (z != null && z.Contains(t)) return z.IsWallAt(bench, zBase, minSlope);
            return globalIsWall;
        }

        int faceN = 0, skipFlat = 0, skipNoWall = 0, skipShort = 0;
        // [교차검증] 기하 판정(실제 링 모양)과 구간 규칙의 의도가 어긋난 정점 수.
        //   둘이 크게 갈리면 링이 구간대로 안 만들어졌다는 뜻 — 다음 로그 한 줄로 갈린다.
        int disagree = 0, checkedPts = 0;
        double minRunLen = 0.5;   // 이보다 짧은 조각은 벽으로 세울 수 없다(판넬 한 장도 못 들어감)

        for (int k = 1; k < rings.Count; k += 2)
        {
            var crest = rings[k];
            var toe = rings[k - 1];
            if (crest == null || toe == null || crest.Count < 2 || toe.Count < 2) continue;
            double h = System.Math.Abs(MeanZ(crest) - MeanZ(toe));
            if (h < 0.1) { skipFlat++; continue; }        // 소단(평탄) 쌍 — 벽면이 아니다
            faceN++;
            int bench = (k - 1) / 2;

            // ★ '여기가 옹벽인가'는 **기하로 직접 잰다** — 토우↔크레스트 수평 간격이 곧 그 면의 수평 물림이고,
            //   벽이면 minSlope×높이(1:0.05·5m → 0.25m), 사면이면 구배n×높이(1:1.5 → 7.5m)다. 30배 차이라 확실하다.
            //   ※ 호길이 param으로 가르면 **전환부에서 틀린다** — 사면 쪽 링은 바깥으로 크게 부풀어 있어
            //     그 정점을 경계에 투영하면 구간 안으로 들어와 버린다(실측: 사면 정점이 옹벽 구간으로 오분류,
            //     간격 7.5m짜리가 옹벽선에 섞였다). 기하는 지표면이 실제로 어떻게 생겼는지를 그대로 반영한다 —
            //     JACK 요구('최종 지표면의 옹벽선')에도 이쪽이 맞다.
            double wallGap = minSlope * h;
            double gapLim = wallGap * 1.05 + 1e-3;
            int n = crest.Count;
            bool closed = Dist2D(crest[0], crest[n - 1]) < 1e-6;
            int m = closed ? n - 1 : n;

            // ★ 판정은 **정점이 아니라 세그먼트 중점**으로 한다.
            //   볼록 모서리를 직각(마이터)으로 만들면 그 **정점만** 바깥으로 더 나가 간격이 커진다
            //   (90° 모서리면 0.25 → 0.25/cos45° = 0.354m). 정점으로 재면 그 한 점이 '벽 아님'으로 떨어져
            //   **모서리마다 벽이 끊기고 코너에 벽이 없어진다**(첫 구현에서 전체 옹벽이 12줄 대신 48줄로 쪼개졌다).
            //   세그먼트 중점은 곧은 구간의 참값(0.25m)을 그대로 주고, 모서리 정점은 양옆 세그먼트가
            //   벽이면 자동으로 포함된다.
            int segN = closed ? m : m - 1;
            if (segN < 1) { skipShort++; continue; }
            var segWall = new bool[segN];
            bool any = false;
            for (int s = 0; s < segN; s++)
            {
                var a = crest[s]; var b = crest[(s + 1) % m];
                var mid = new Point3((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
                segWall[s] = Dist2D(mid, NearestOn(toe, mid)) <= gapLim;
                any |= segWall[s];
                // 의도(구간 규칙)와 실제(링 모양)가 어긋나는지 세어 둔다 — 판정에는 쓰지 않는다.
                checkedPts++;
                if (segWall[s] != IsWall(GradingGeometry.ParamAt(boundary, cum, mid.X, mid.Y), bench))
                    disagree++;
            }
            if (!any) { skipNoWall++; continue; }

            var toePt = new Point3[m];
            for (int i = 0; i < m; i++) toePt[i] = NearestOn(toe, crest[i]);

            foreach (var seg in SegRunsToVertexRuns(segWall, closed, m))
            {
                var cr = new List<Point3>(seg.Count);
                var to = new List<Point3>(seg.Count);
                foreach (var idx in seg) { cr.Add(crest[idx]); to.Add(toePt[idx]); }
                if (cr.Count < 2) { skipShort++; continue; }
                double len = 0;
                for (int i = 0; i + 1 < cr.Count; i++) len += Dist2D(cr[i], cr[i + 1]);
                if (len < minRunLen) { skipShort++; continue; }

                outp.Add(new WallRun { Up = up, Bench = bench, Toe = to, Crest = cr, Height = h });
            }
        }

        LastDiag = $"옹벽선 {outp.Count}줄 · 벽면쌍 {faceN} · 건너뜀(평탄 {skipFlat} · 옹벽아님 {skipNoWall} · 짧음 {skipShort})" +
                   $" · 전역 1:{globalSlope}{(globalIsWall ? "(수직)" : "")} · 구간 {(zones?.Count ?? 0)}개" +
                   $" · 기하↔구간 불일치 {disagree}/{checkedPts}점";
        return outp;
    }

    /// <summary>
    /// [이어서 하기 0805] 앞 구역의 옹벽선에서 <b>새 구역이 덮은 부분을 잘라낸다</b>.
    /// <para>
    /// 새 구역이 앞 구역 사면 위에 얹히면 그 자리 옹벽은 최종 지표면에 더 이상 없다.
    /// 정지면을 만드는 그 순간 앞 구역의 저장된 옹벽선을 갱신해 두면, 내보내기 시점엔 이미 최종 상태라
    /// <b>지우개(마스크)가 필요 없다</b> — 종전 결함(지우개 경계에 조각이 남음)의 뿌리를 없앤다.
    /// </para>
    /// 잘린 뒤 남은 조각이 <paramref name="minLen"/>보다 짧으면 버린다(벽 한 장도 못 세운다).
    /// </summary>
    /// <param name="runs">앞 구역의 옹벽선.</param>
    /// <param name="covered">(x,y)가 새 구역에 덮였는가 — 덮인 곳은 옹벽선에서 제외한다.</param>
    public static List<WallRun> TrimBy(IReadOnlyList<WallRun>? runs, System.Func<double, double, bool>? covered,
                                       double minLen = 0.5)
    {
        var outp = new List<WallRun>();
        if (runs == null) return outp;
        if (covered == null) { outp.AddRange(runs); return outp; }
        int cut = 0, kept = 0, dropped = 0;
        foreach (var r in runs)
        {
            int n = System.Math.Min(r.Crest.Count, r.Toe.Count);
            if (n < 2) continue;
            // 세그먼트 단위 판정 — 중점이 덮였으면 그 세그먼트는 사라진 것으로 본다(정점 판정은 경계에서 흔들린다).
            var keep = new bool[n - 1];
            bool anyKeep = false, anyCut = false;
            for (int i = 0; i + 1 < n; i++)
            {
                double mx = (r.Crest[i].X + r.Crest[i + 1].X) / 2, my = (r.Crest[i].Y + r.Crest[i + 1].Y) / 2;
                keep[i] = !covered(mx, my);
                anyKeep |= keep[i]; anyCut |= !keep[i];
            }
            if (!anyKeep) { dropped++; continue; }
            if (!anyCut) { outp.Add(r); kept++; continue; }
            cut++;
            int s = 0;
            while (s < keep.Length)
            {
                if (!keep[s]) { s++; continue; }
                int e = s;
                while (e + 1 < keep.Length && keep[e + 1]) e++;
                var to = new List<Point3>(); var cr = new List<Point3>();
                for (int i = s; i <= e + 1; i++) { to.Add(r.Toe[i]); cr.Add(r.Crest[i]); }
                double len = 0;
                for (int i = 0; i + 1 < cr.Count; i++) len += Dist2D(cr[i], cr[i + 1]);
                if (len >= minLen)
                    outp.Add(new WallRun { Up = r.Up, Bench = r.Bench, Height = r.Height, Toe = to, Crest = cr });
                else dropped++;
                s = e + 2;
            }
        }
        LastDiag = $"옹벽선 갱신 — 그대로 {kept}줄 · 잘림 {cut}줄 · 버림 {dropped}조각 → 결과 {outp.Count}줄";
        return outp;
    }

    private static double MeanZ(IReadOnlyList<Point3> r)
    {
        double s = 0;
        foreach (var p in r) s += p.Z;
        return s / System.Math.Max(r.Count, 1);
    }

    private static double Dist2D(Point3 a, Point3 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// '벽인 세그먼트'의 연속 묶음을 **정점 인덱스 목록**으로 바꾼다.
    /// 세그먼트 s는 정점 s와 s+1을 잇는다 — 연속 세그먼트 [s0..s1]의 정점은 [s0 .. s1+1].
    /// 닫힌 고리에서 전부 벽이면 한 바퀴를 한 줄로 돌려준다(시작점에서 끊기지 않게).
    /// </summary>
    internal static List<List<int>> SegRunsToVertexRuns(bool[] segWall, bool closed, int m)
    {
        var res = new List<List<int>>();
        int segN = segWall.Length;
        if (segN == 0) return res;
        bool all = true;
        foreach (var f in segWall) all &= f;
        if (all)
        {
            var one = new List<int>(m + 1);
            for (int i = 0; i < m; i++) one.Add(i);
            if (closed) one.Add(0);                      // 한 바퀴 — 고리를 닫는다
            res.Add(one);
            return res;
        }
        int start = 0;
        while (start < segN && segWall[start]) start++;   // false에서 시작해 랩을 자연스럽게 처리
        var cur = new List<int>();
        for (int k = 0; k < segN; k++)
        {
            int s = (start + k) % segN;
            if (segWall[s])
            {
                if (cur.Count == 0) cur.Add(s);
                cur.Add((s + 1) % m);
            }
            else if (cur.Count > 0) { res.Add(cur); cur = new List<int>(); }
        }
        if (cur.Count > 0) res.Add(cur);
        // ※ 벽이 아닌 이웃 정점을 '한 칸 더' 붙이지 않는다 — 그 정점은 사면이라 토우와의 간격이
        //   30배(0.25m → 7.5m)로 튀어 옹벽선이 통째로 일그러진다(첫 구현에서 실제로 그랬다).
        return res;
    }

    /// <summary>폴리선 위에서 점 q에 가장 가까운 점(2D 최근접, Z는 그 세그먼트에서 보간).</summary>
    internal static Point3 NearestOn(IReadOnlyList<Point3> line, Point3 q)
    {
        double best = double.MaxValue;
        Point3 bp = line[0];
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy;
            double t = L2 > 1e-12 ? ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2 : 0;
            t = System.Math.Clamp(t, 0, 1);
            double px = a.X + dx * t, py = a.Y + dy * t;
            double d = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
            if (d < best) { best = d; bp = new Point3(px, py, a.Z + (b.Z - a.Z) * t); }
        }
        return bp;
    }
}
