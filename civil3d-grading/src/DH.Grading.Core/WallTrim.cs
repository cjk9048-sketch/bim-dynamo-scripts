namespace DH.Grading.Core;

/// <summary>★★★[JACK 0916] <b>줄마다 3D로 자른다 — 데이라잇을 따로 딸 필요가 없다.</b>
///
/// <para>JACK: <i>"위에서 아래로 <b>투영하듯이</b> 자르니깐 이상하게 나오는 거야. <b>있는 그대로</b> 해야 하는데."</i>
/// 그리고 <i>"지금 우리는 <b>데이라잇과의 싸움</b>이야."</i></para>
///
/// <b>왜 평면으로는 안 되나 — 숫자가 말한다</b>
///
/// <para>옹벽은 <c>1:0.01</c>이다. 단높이 5m면 면 폭이 <b>0.05m</b>다
/// (<c>MinSlope 0.01</c> · <c>MinFaceRun 0.005</c> · <c>faceRun = max(benchH×slope, MinFaceRun)</c>).
/// 즉 <b>5m의 높이가 평면에서 5cm</b>다. 그런데 판정이 필요한 바로 그 선(데이라잇)이 하필 그 5cm 안에 있다.
/// 평면에서 도는 연산 — 폴리고나이즈 · 안쪽점 · 격자표본 · 표고조회 — 이 전부 <b>5cm 표적</b>을 맞춰야 하고,
/// 5cm 옆으로 어긋나면 <b>z가 5m 튄다</b>.</para>
///
/// <para>0914~0916에 그 길로 열 번 넘게 고쳤다. 증상은 매번 달랐지만 뿌리는 하나였다 —
/// 가짜 데이라잇 30%, 표결이 갈리는 조각, 뚜껑의 8~14m 절벽, 평면에서 안 맞는 데이라잇.</para>
///
/// <b>그래서 줄을 따라 1차원으로 푼다</b>
///
/// <para><see cref="GradingGeometry.WallInWedge"/>가 낸 줄은 <b>표고가 일정한 폴리선</b>이다(등고선).
/// 그 줄을 따라가며 <c>원지반(x,y)</c>과 <c>정지면(x,y)</c>을 재면,
/// <b>줄이 두 면 사이에 있는 구간</b>이 곧 남길 벽이고
/// <b>그 구간의 양 끝</b>이 곧 데이라잇 점이다 — 선형보간 한 번이면 정확히 찍힌다.</para>
///
/// <code>
///   줄(z 일정) ────●━━━━━━━━━━━━━●────
///                  ↑             ↑
///            정지면과 만남   원지반과 만남     ← 이 점들이 데이라잇이다
/// </code>
///
/// <para>NTS도, 폴리고나이즈도, 다수결도, Hide 도넛도 <b>필요 없다</b>.
/// 그리고 자가검증이 공짜다 — <b>끝점 위에서 두 면의 표고차는 정의상 0</b>이다.</para></summary>
public static class WallTrim
{
    /// <summary>줄 하나에서 잘라 낸 조각.</summary>
    public sealed class Chain
    {
        /// <summary>줄 번호(0 = 가장 안쪽 줄).</summary>
        public int Row;
        /// <summary>남긴 점들(끝점은 데이라잇 위에 정확히 앉는다).</summary>
        public List<Point3> Pts = new();
        /// <summary>줄 위에서 차지한 구간 — 줄 길이에 대한 비(0~1). 줄끼리 짝짓는 데 쓴다.</summary>
        public double S0, S1;
        /// <summary>양 끝이 <b>어느 면</b>과 만나서 끊겼나(진단·검증용).</summary>
        public string EndA = "", EndB = "";
    }

    /// <summary>한 덩이 — 줄 조각들이 위아래로 이어진 것.</summary>
    public sealed class Lobe
    {
        public List<Chain> Chains = new();
        /// <summary>바깥 테두리(데이라잇 + 양 옆).</summary>
        public List<Point3> Ring = new();
        /// <summary>테두리 중 <b>데이라잇</b>인 두 줄(진단·검증용).</summary>
        public List<Point3> SideA = new(), SideB = new();
    }

    /// <summary>★<b>줄마다 잘라 낸다.</b>
    ///
    /// <para>남기는 규칙은 <b>방향에 안 기댄다</b> — 벽은 <b>원지반과 정지면 사이</b>에 서 있다.
    /// 절토면 정지면이 아래고 원지반이 위, 성토면 그 반대인데,
    /// <c>min/max</c>로 쓰면 <b>둘 다 한 줄</b>로 맞는다.
    /// (0916에 절성토를 갈라 쓰다 한쪽을 안 뒤집어 벽이 통째로 빠질 뻔했다.)</para></summary>
    /// <param name="rows">옹벽의 줄들(안쪽부터). 줄마다 표고가 일정해야 한다.</param>
    /// <param name="ground">원지반 표고 — 못 재면 <c>null</c>.</param>
    /// <param name="design">정지면(최종지표면) 표고 — 못 재면 <c>null</c>.</param>
    /// <param name="step">줄을 훑는 간격(m). 소단 폭보다 잘게.</param>
    /// <param name="tol">표고 허용오차(m).</param>
    public static List<Lobe> Trim(
        IReadOnlyList<IReadOnlyList<Point3>> rows,
        Func<double, double, double?> ground,
        Func<double, double, double?> design,
        double step, double tol, out string log)
    {
        var sb = new System.Text.StringBuilder();
        var byRow = new List<List<Chain>>();
        int nMissG = 0, nMissD = 0, nSamp = 0, nEndChk = 0;
        double worstEnd = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = Densify(rows[i], step);
            var cum = new double[row.Count];
            for (int k = 1; k < row.Count; k++)
                cum[k] = cum[k - 1] + Dist(row[k - 1], row[k]);
            double total = cum[row.Count - 1];
            var keep = new bool[row.Count];
            for (int k = 0; k < row.Count; k++)
            {
                nSamp++;
                double? g = ground(row[k].X, row[k].Y);
                double? d = design(row[k].X, row[k].Y);
                if (g == null) nMissG++;
                if (d == null) nMissD++;
                // ★못 재면 <b>안 남긴다</b> — 모르는 자리에 벽을 세우지 않는다.
                if (g == null || d == null) { keep[k] = false; continue; }
                double lo = Math.Min(g.Value, d.Value), hi = Math.Max(g.Value, d.Value);
                keep[k] = row[k].Z >= lo - tol && row[k].Z <= hi + tol;
            }

            var chains = new List<Chain>();
            int s = -1;
            for (int k = 0; k <= row.Count; k++)
            {
                bool on = k < row.Count && keep[k];
                if (on && s < 0) s = k;
                else if (!on && s >= 0)
                {
                    int e = k - 1;
                    var c = new Chain { Row = i };
                    // ★양 끝을 <b>데이라잇 위로 옮긴다</b> — 표본 격자가 아니라 진짜 만나는 점으로.
                    string wA = "줄끝", wB = "줄끝";
                    var pA = s > 0 ? Cross(row[s - 1], row[s], ground, design, tol, out wA) : row[s];
                    var pB = e < row.Count - 1 ? Cross(row[e + 1], row[e], ground, design, tol, out wB) : row[e];
                    c.EndA = wA; c.EndB = wB;
                    c.Pts.Add(pA);
                    for (int q = s; q <= e; q++) c.Pts.Add(row[q]);
                    c.Pts.Add(pB);
                    c.S0 = total > 1e-9 ? cum[s] / total : 0;
                    c.S1 = total > 1e-9 ? cum[e] / total : 1;
                    if (Len(c.Pts) >= step) chains.Add(c);
                    // ★끝점 자가검증 — <b>잘린 끝만</b> 잰다.
                    //   줄이 그냥 끝난 자리("줄끝")는 데이라잇이 아니다 — 그것까지 재면
                    //   <b>아무 뜻 없는 큰 수</b>가 나와 잣대가 망가진다(S127이 그걸 잡았다).
                    foreach (var (pp, wh) in new[] { (pA, wA), (pB, wB) })
                    {
                        if (wh != "원지반" && wh != "정지면") continue;
                        double? v2 = wh == "원지반" ? ground(pp.X, pp.Y) : design(pp.X, pp.Y);
                        if (v2 == null) continue;
                        double e2 = Math.Abs(pp.Z - v2.Value);
                        if (e2 > worstEnd) worstEnd = e2;
                        nEndChk++;
                    }
                    s = -1;
                }
            }
            byRow.Add(chains);
        }

        // ── 줄 조각을 <b>덩이</b>로 묶는다 — 위아래 줄에서 겹치는 구간끼리 ──
        var lobes = new List<Lobe>();
        var openL = new List<Lobe>();
        for (int i = 0; i < byRow.Count; i++)
        {
            var used = new bool[byRow[i].Count];
            var nextOpen = new List<Lobe>();
            foreach (var lb in openL)
            {
                var last = lb.Chains[lb.Chains.Count - 1];
                int best = -1; double bestOv = 0;
                for (int k = 0; k < byRow[i].Count; k++)
                {
                    if (used[k]) continue;
                    double ov = Math.Min(last.S1, byRow[i][k].S1) - Math.Max(last.S0, byRow[i][k].S0);
                    if (ov > bestOv) { bestOv = ov; best = k; }
                }
                if (best >= 0) { used[best] = true; lb.Chains.Add(byRow[i][best]); nextOpen.Add(lb); }
                else lobes.Add(lb);          // 더 이어지지 않는다 — 닫는다
            }
            for (int k = 0; k < byRow[i].Count; k++)
                if (!used[k]) { var lb = new Lobe(); lb.Chains.Add(byRow[i][k]); nextOpen.Add(lb); }
            openL = nextOpen;
        }
        lobes.AddRange(openL);

        // ── 덩이마다 테두리를 짓는다 ──
        foreach (var lb in lobes)
        {
            var c0 = lb.Chains[0];
            var cN = lb.Chains[lb.Chains.Count - 1];
            var ring = new List<Point3>();
            foreach (var q in c0.Pts) ring.Add(q);                      // ①가장 안쪽 줄
            for (int i = 1; i < lb.Chains.Count; i++)                    // ②한쪽 데이라잇(끝점들)
            { var q = lb.Chains[i].Pts[lb.Chains[i].Pts.Count - 1]; ring.Add(q); lb.SideB.Add(q); }
            for (int k = cN.Pts.Count - 2; k >= 1; k--) ring.Add(cN.Pts[k]);   // ③가장 바깥 줄(되짚기)
            for (int i = lb.Chains.Count - 1; i >= 0; i--)               // ④반대쪽 데이라잇(시작점들)
            { var q = lb.Chains[i].Pts[0]; ring.Add(q); lb.SideA.Add(q); }
            lb.Ring = Weed(ring);
        }
        lobes.RemoveAll(l => l.Ring.Count < 4);

        sb.Append($"줄 {rows.Count}개를 훑어 조각 ");
        int nch = 0; foreach (var r in byRow) nch += r.Count;
        sb.Append($"<b>{nch}개</b> → 덩이 <b>{lobes.Count}개</b>");
        sb.Append($" · 훑은 점 {nSamp}");
        if (nMissG > 0 || nMissD > 0) sb.Append($" · 못 잰 점 원지반 {nMissG}/정지면 {nMissD}");
        sb.Append(nEndChk > 0
            ? $" · <b>데이라잇 끝점 {nEndChk}개 자가검증 최악 {worstEnd:F3}m</b>(정의상 0이어야 한다)"
            : " · 잘린 끝이 없다(줄이 전부 두 면 사이에 있다)");
        log = sb.ToString();
        return lobes;
    }

    /// <summary>표고가 안 맞는 쪽 점과 맞는 쪽 점 사이에서 <b>만나는 자리</b>를 선형보간으로 찾는다.
    /// <para>줄은 표고가 일정하므로 변하는 것은 <b>지형</b>뿐이다 — 1차원 근찾기다.</para></summary>
    private static Point3 Cross(Point3 outside, Point3 inside,
                                Func<double, double, double?> ground, Func<double, double, double?> design,
                                double tol, out string which)
    {
        which = "?";
        double z = inside.Z;
        // 어느 면을 넘었나 — 바깥 점에서 더 크게 벗어난 쪽
        double? go = ground(outside.X, outside.Y), doo = design(outside.X, outside.Y);
        if (go == null || doo == null) return inside;
        double lo = Math.Min(go.Value, doo.Value), hi = Math.Max(go.Value, doo.Value);
        bool overHi = z > hi;                       // 위로 벗어났다
        Func<double, double, double?> f = overHi
            ? (Math.Abs(hi - go.Value) < 1e-9 ? ground : design)
            : (Math.Abs(lo - go.Value) < 1e-9 ? ground : design);
        which = (f == ground) ? "원지반" : "정지면";
        double a = 0, b = 1;                        // a=inside(안), b=outside(밖)
        for (int it = 0; it < 30; it++)
        {
            double m = (a + b) * 0.5;
            double x = inside.X + (outside.X - inside.X) * m;
            double y = inside.Y + (outside.Y - inside.Y) * m;
            double? v = f(x, y);
            if (v == null) { b = m; continue; }
            bool stillIn = overHi ? z <= v.Value + tol : z >= v.Value - tol;
            if (stillIn) a = m; else b = m;
            if (b - a < 1e-4) break;
        }
        double t2 = (a + b) * 0.5;
        return new Point3(inside.X + (outside.X - inside.X) * t2,
                          inside.Y + (outside.Y - inside.Y) * t2, z);
    }

    private static double Dist(Point3 a, Point3 b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Len(IReadOnlyList<Point3> p)
    { double s = 0; for (int i = 1; i < p.Count; i++) s += Dist(p[i - 1], p[i]); return s; }

    private static List<Point3> Densify(IReadOnlyList<Point3> p, double step)
    {
        var o = new List<Point3>();
        if (p.Count == 0) return o;
        o.Add(p[0]);
        for (int i = 1; i < p.Count; i++)
        {
            double d = Dist(p[i - 1], p[i]);
            int n = Math.Max(1, (int)Math.Ceiling(d / Math.Max(0.05, step)));
            for (int k = 1; k <= n; k++)
            {
                double t = (double)k / n;
                o.Add(new Point3(p[i - 1].X + (p[i].X - p[i - 1].X) * t,
                                 p[i - 1].Y + (p[i].Y - p[i - 1].Y) * t, p[i].Z));
            }
        }
        return o;
    }

    private static List<Point3> Weed(List<Point3> p)
    {
        var o = new List<Point3>();
        foreach (var q in p)
            if (o.Count == 0 || Dist(o[o.Count - 1], q) > 1e-6) o.Add(q);
        return o;
    }
}
