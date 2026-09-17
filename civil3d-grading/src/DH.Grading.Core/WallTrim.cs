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

        /// <summary>★★★[검토 0916 · 치명] <b>이웃한 두 줄 사이의 띠</b>들. 이것이 <b>진짜 벽 자리</b>다.
        ///
        /// <para><b>왜 따로 두나.</b> <see cref="Ring"/>은 「첫 줄 전부 → 끝점들 → 마지막 줄 되짚기 →
        /// 첫점들」로 두른 선이다. 가운데 줄들을 <b>건너뛴 현(弦)</b>이라, 벽이 ㄷ자이고 날개가
        /// 데이라잇까지(23~28m) 뻗으면 그 현이 <b>벽 없는 땅을 가로질러 삼킨다</b>.</para>
        ///
        /// <para><b>계측</b>(현장 좌표 재현): 덩이 링 <b>759.9㎡</b> vs 발자국 <b>404.9㎡</b> —
        /// 발자국보다 6배 깊고 바깥으로 <b>26.3m</b>. 덩이 ∖ 발자국 = <b>485.6㎡</b>.
        /// 아무것도 안 자르면 0.0㎡다 — <b>자르기 시작해야 터진다</b>.</para>
        ///
        /// <code>
        ///   현 하나로 두르기(옛)          띠를 쌓기(지금)
        ///   ┌───────────────┐            ┌───┐
        ///   │╲             ╱│            ├───┤   ← 줄 사이마다 한 장
        ///   │ ╲   벽 없음 ╱ │            ├───┤
        ///   │  ╲_________╱  │            └───┘
        /// </code></summary>
        public List<List<Point3>> Bands = new();
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
        //   ※<see cref="Lobe.Ring"/>은 <b>진단·표시용</b>으로만 남긴다(데이라잇 양옆을 보여 준다).
        //     자르고 나누는 데 쓰는 것은 아래 <see cref="Lobe.Bands"/>다.
        int nThin = 0;
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

            // ── ★<b>띠</b> — 이웃한 두 줄 사이를 한 장씩. 이것이 진짜 벽 자리다 ──
            for (int i = 0; i + 1 < lb.Chains.Count; i++)
            {
                var A = lb.Chains[i].Pts; var B = lb.Chains[i + 1].Pts;
                if (A.Count < 2 || B.Count < 2) continue;
                var band = new List<Point3>(A.Count + B.Count);
                foreach (var q in A) band.Add(q);
                for (int k = B.Count - 1; k >= 0; k--) band.Add(B[k]);
                band = Weed(band);
                if (band.Count >= 4) lb.Bands.Add(band);
            }
            // ★줄이 <b>하나뿐인 덩이</b>는 「갔다 온 선」이라 넓이가 0이다(검토 실측: 링 41점 · 0.000㎡ ·
            //   <c>ToCleanGeometry</c>가 null). 그러면 장부에도 안 잡히고, 쐐기에서도 발자국에서도
            //   안 빠져 <b>Hide에 먹혀 그 줄의 벽이 통째로 지워진다</b>(한 판에서 6덩이 중 3개가 그랬다).
            //   → <b>이웃 줄까지의 실제 거리</b>만큼 폭을 준 띠로 세운다.
            if (lb.Chains.Count == 1)
            {
                var c = lb.Chains[0];
                int rn = NearRow(rows, c.Row);
                var band = rn >= 0 ? WidenToward(c.Pts, rows[rn]) : new List<Point3>();
                if (band.Count >= 4) { lb.Bands.Add(band); nThin++; }
            }
        }
        lobes.RemoveAll(l => l.Ring.Count < 4);

        sb.Append($"줄 {rows.Count}개를 훑어 조각 ");
        int nch = 0; foreach (var r in byRow) nch += r.Count;
        int nBand = 0; foreach (var l in lobes) nBand += l.Bands.Count;
        sb.Append($"<b>{nch}개</b> → 덩이 <b>{lobes.Count}개</b> · 띠 <b>{nBand}장</b>");
        if (nThin > 0) sb.Append($"(줄 하나짜리 <b>{nThin}덩이</b>는 폭을 줬다)");
        sb.Append($" · 훑은 점 {nSamp}");
        if (nMissG > 0 || nMissD > 0) sb.Append($" · 못 잰 점 원지반 {nMissG}/정지면 {nMissD}");
        sb.Append(nEndChk > 0
            ? $" · <b>데이라잇 끝점 {nEndChk}개 자가검증 최악 {worstEnd:F3}m</b>(정의상 0이어야 한다)"
            : " · 잘린 끝이 없다(줄이 전부 두 면 사이에 있다)");
        log = sb.ToString();
        return lobes;
    }

    /// <summary>★★★[JACK 0916 <i>"뚜껑 바깥변까지 해"</i>] <b>쐐기의 바깥 변을 진짜 데이라잇으로 딴다.</b>
    ///
    /// <para>여태 쐐기의 바깥 변은 <b>흉내</b>였다 — 둘레를 129칸으로 나눠 데이라잇 거리를 재고
    /// 못 잰 칸은 이웃 값으로 메웠다(<c>farAt</c>). 그러니 진짜 곡선과 어긋나는 것이 당연했고,
    /// JACK이 스샷에 <i>"미세하게 맞지 않음 · 튀어나옴"</i>이라 짚은 것이 그것이다.</para>
    ///
    /// <para>★<b>벽과 똑같은 방법</b>을 쓴다. 벽은 「줄을 따라가며 지형과 견주는」 1차원 문제였다.
    /// 뚜껑의 바깥 변은 「<b>직각선을 따라가며 정지면과 원지반을 견주는</b>」 1차원 문제다 —
    /// 둘이 만나는 자리가 곧 <b>정지 데이라잇</b>이고, 거기가 뚜껑의 끝이다.</para>
    ///
    /// <code>
    ///   찍은 선 ├──────→ 바깥으로  ●  ← 정지면 = 원지반 (여기가 데이라잇)
    ///                              그 너머는 손대지 않은 땅이다
    /// </code>
    ///
    /// <para>표본 간격과 무관하다 — 자리마다 <b>직접 찾기</b> 때문이다.</para></summary>
    /// <param name="ruler">자(구간을 재는 폴리곤).</param>
    /// <param name="cum">자의 누적 길이.</param>
    /// <param name="t0">구간 시작 둘레값.</param>
    /// <param name="span">구간 길이.</param>
    /// <param name="maxD">바깥으로 찾아볼 최대 거리(m).</param>
    /// <param name="nT">구간을 몇 칸으로 훑을지.</param>
    /// <returns>구간을 따라간 <b>데이라잇 점들</b>(t0 → t0+span 차례).</returns>
    /// <param name="nMiss">못 찾은 자리 수 — <b>0이 아니면 그만큼 이웃으로 메웠다</b>는 뜻이다.</param>
    /// <param name="maxStep">이웃한 두 점 사이 <b>가장 긴 걸음</b>(m). 링이 어딘가를 가로질렀는지 보는 잣대.</param>
    /// <param name="nMulti">뿌리가 <b>둘 이상</b>이던 자리 수(소단이 지형을 두 번 스친 자리).</param>
    public static List<Point3> DaylightOutward(
        IReadOnlyList<Point3> ruler, double[] cum, double t0, double span,
        Func<double, double, double?> ground, Func<double, double, double?> design,
        double maxD, int nT, double step, double z,
        out int nMiss, out double maxStep, out int nMulti, out string log)
    {
        var res = new List<Point3>();
        double tot = cum[cum.Length - 1];
        nMulti = 0;

        // ── ①자리마다 <b>거리</b>를 찾는다. 못 찾으면 null로 <b>자리를 비워 둔다</b> ──
        //   ★★★[검토 0916 · 치명1] 종전엔 못 찾은 자리를 <b>소리 없이 건너뛰었다</b>.
        //     안쪽 사슬은 늘 nT+1점인데 바깥 사슬만 짧아져, 되짚는 변이 그 자리를
        //     <b>곧은 선으로 가로질렀다</b>(검토 실측: 한 걸음 <b>17.19m</b> · 정상 칸은 1.00m ·
        //     25자리 중 2곳만 찾고도 넓이 관문을 통과했다).
        var dist = new double?[nT + 1];
        for (int i = 0; i <= nT; i++)
        {
            double t = t0 + span * i / nT;
            double tw = ((t % tot) + tot) % tot;
            // ══ ★★★[검토 0916 · 치명 · <b>내가 만든 회귀</b>] <b>0은 부호가 바뀐 게 아니다.</b>
            //
            //   <para><b>데이라잇 <u>바깥</u>에서는 정지면이 곧 원지반</b>이다 — 이 저장소가 스스로 적어 뒀다:
            //   <i>"합성면은 원지반을 깔고 시작하므로 정지 바깥에서도 값이 나오고, <b>그 값은 원지반과 같다</b>"</i>.
            //   즉 <c>diff = 정지면 − 원지반</c>이 데이라잇 너머로 <b>쭉 0</b>이다.</para>
            //
            //   <para>그런데 관문이 «곱 ≤ 0»이라 <b>0도 뿌리로 쳤다</b>. v95.2는 첫 뿌리에서 <c>break</c>해
            //   이 성질이 안 드러났는데, 「가장 바깥 뿌리」로 바꾸며 break를 걷자
            //   <b>찾을거리 끝까지 계속 덮어썼다</b>. 검토 실측(참 데이라잇 10.00m):</para>
            //
            //   <code>
            //     찾을거리 80m → 딴 거리 <b>79.50m</b>    단순 O · 한걸음 O · 못찾음 O → <b>갈아 끼웠다</b>
            //     찾을거리 40m → 딴 거리 <b>39.50m</b>    새 관문 넷 중 <b>셋이 통과</b>한다
            //   </code>
            //
            //   <para>바깥 변이 <b>고르게</b> 밀려나 완벽히 매끈하므로 한 걸음·단순·못찾음이 다 통과한다.
            //   (같이 올린 「찾을거리 바닥 20→80m」가 이 증상을 <b>키웠다</b> — 멀리 볼수록 더 멀리 달아난다.)</para>
            //
            //   <para>★<b>데이라잇은 「두 면이 갈라지기를 멈추는 자리」</b>다. 그러니 판정은 하나다 —
            //   <c>|diff|</c>가 <b>처음 0으로 잦아드는 자리</b>. 참 부호 뒤집힘도 같은 자리에서 잡힌다.
            //   평평한 0이 <b>이어지면</b> 정지 구역 밖이니 더 볼 것이 없다 — <b>멈춘다</b>.</para>
            const double ZT = 0.005;                      // 이보다 작으면 "같은 면"으로 본다(5mm)
            double? found = null; int roots = 0; double lastRoot = double.NegativeInfinity;
            int prevSign = 0; bool havePrev = false; int flatRun = 0; double prevD = 0;
            for (double d = 0; d <= maxD + 1e-9; d += step)
            {
                var q = GradingGeometry.OutwardAt(ruler, cum, tw, d);
                double? g = ground(q.X, q.Y), de = design(q.X, q.Y);
                if (g == null || de == null) { havePrev = false; flatRun = 0; prevD = d; continue; }
                double diff = de.Value - g.Value;            // 정지면 − 원지반
                int sg = diff > ZT ? 1 : (diff < -ZT ? -1 : 0);
                if (havePrev && prevSign != 0 && sg != prevSign && d > 0)
                {
                    // ★<b>이분법</b> — 「여기부터 같은 면」인 자리를 정확히 좁힌다.
                    double a2 = prevD, b2 = d;
                    for (int it = 0; it < 30 && b2 - a2 > 1e-4; it++)
                    {
                        double m = (a2 + b2) * 0.5;
                        var qm = GradingGeometry.OutwardAt(ruler, cum, tw, m);
                        double? gm = ground(qm.X, qm.Y), dm = design(qm.X, qm.Y);
                        if (gm == null || dm == null) { b2 = m; continue; }
                        double dv = dm.Value - gm.Value;
                        int sm = dv > ZT ? 1 : (dv < -ZT ? -1 : 0);
                        if (sm == prevSign) a2 = m; else b2 = m;
                    }
                    // ★소단이 원지반을 <b>두 번</b> 스치면 뿌리가 둘이다 — 뚜껑의 끝은 <b>바깥</b> 것이다.
                    //   한 걸음 안에 든 두 번째는 같은 뿌리로 본다(표본이 뿌리에 딱 떨어질 때 생긴다).
                    double rd = (a2 + b2) * 0.5;
                    if (rd - lastRoot > step * 1.5) roots++;
                    lastRoot = rd; found = rd;
                }
                if (sg == 0)
                {
                    // ★평평한 0이 <b>이어지면</b> 정지 구역 밖이다 — 더 가 봐야 같은 값뿐이다.
                    flatRun++;
                    if (found != null && flatRun >= 3) break;
                }
                else flatRun = 0;
                prevSign = sg; prevD = d; havePrev = true;
            }
            if (roots > 1) nMulti++;
            dist[i] = found;
        }

        // ── ②빈 자리를 <b>이웃으로 메운다</b> — 자리와 점의 짝이 어긋나지 않게 ──
        int hit = 0; foreach (var v in dist) if (v != null) hit++;
        nMiss = nT + 1 - hit;
        if (hit == 0)
        {
            nMiss = nT + 1; maxStep = 0;
            log = $"<b>⚠한 자리도 못 찾았다</b>(훑은 자리 {nT + 1} · 찾아본 거리 {maxD:F0}m)";
            return res;
        }
        for (int i = 0; i <= nT; i++)
        {
            if (dist[i] != null) continue;
            int lo = i, hi = i;
            while (lo >= 0 && dist[lo] == null) lo--;
            while (hi <= nT && dist[hi] == null) hi++;
            if (lo < 0 && hi > nT) continue;
            if (lo < 0) dist[i] = dist[hi];
            else if (hi > nT) dist[i] = dist[lo];
            else dist[i] = dist[lo]!.Value + (dist[hi]!.Value - dist[lo]!.Value) * (i - lo) / (double)(hi - lo);
        }

        for (int i = 0; i <= nT; i++)
        {
            double t = t0 + span * i / nT;
            double tw = ((t % tot) + tot) % tot;
            var pt = GradingGeometry.OutwardAt(ruler, cum, tw, dist[i]!.Value);
            res.Add(new Point3(pt.X, pt.Y, z));
        }

        // ── ③<b>진짜 잣대</b> — 이웃한 두 점 사이 가장 긴 걸음 ──
        //   ★★★[검토 0916 · 치명2] 종전의 "자가검증 최악 0.000m"은 <b>아무것도 검증하지 않았다</b>.
        //     찾은 점에서 |원지반−정지면|을 쟀는데 그 점은 <b>정의상 뿌리</b>라 늘 0이다 —
        //     링이 17m를 가로지르고 25자리 중 2곳만 찾은 판에서도 0.000m가 찍혔다.
        maxStep = 0;
        for (int i = 1; i < res.Count; i++)
        {
            double dx = res[i].X - res[i - 1].X, dy = res[i].Y - res[i - 1].Y;
            maxStep = Math.Max(maxStep, Math.Sqrt(dx * dx + dy * dy));
        }
        double nominal = span / Math.Max(1, nT);
        log = $"자리 {nT + 1} 중 <b>{hit}곳</b>에서 찾음"
            + (nMiss > 0 ? $" · <b>못 찾음 {nMiss}</b>(이웃으로 메웠다)" : "")
            + (nMulti > 0 ? $" · 뿌리 둘 이상 {nMulti}곳(가장 바깥을 씀)" : "")
            + $" · <b>한 걸음 최악 {maxStep:F2}m</b>(칸 {nominal:F2}m)";
        return res;
    }

    /// <summary>줄 <paramref name="r"/>의 <b>이웃 줄</b> 번호 — 없으면 −1.</summary>
    private static int NearRow(IReadOnlyList<IReadOnlyList<Point3>> rows, int r)
    {
        if (r + 1 < rows.Count && rows[r + 1].Count >= 2) return r + 1;
        if (r - 1 >= 0 && rows[r - 1].Count >= 2) return r - 1;
        return -1;
    }

    /// <summary>줄 하나짜리 덩이에 <b>이웃 줄 쪽으로만</b> 폭을 준다.
    ///
    /// <para>★<b>양쪽으로 넓히면 안 된다.</b> 벽은 두 줄 <b>사이</b>에만 있다 —
    /// 반대쪽으로 넓히면 벽이 없는 땅을 제 몫이라 주장하고, 그만큼 발자국 밖으로 삐져나간다
    /// (하네스 S130 실측: 좌우로 넓혔더니 발자국 밖 <b>19.9㎡</b>).</para></summary>
    private static List<Point3> WidenToward(IReadOnlyList<Point3> line, IReadOnlyList<Point3> target)
    {
        int n = line.Count;
        var res = new List<Point3>();
        if (n < 2 || target.Count < 1) return res;
        var far = new List<Point3>(n);
        for (int i = 0; i < n; i++)
        {
            // 이웃 줄에서 <b>가장 가까운 점</b> 쪽으로 민다 — 그 자리가 곧 띠의 반대 변이다.
            double bx = 0, by = 0, best = double.MaxValue;
            foreach (var q in target)
            {
                double dx = q.X - line[i].X, dy = q.Y - line[i].Y, d2 = dx * dx + dy * dy;
                if (d2 < best) { best = d2; bx = q.X; by = q.Y; }
            }
            if (best == double.MaxValue) return new List<Point3>();
            far.Add(new Point3(bx, by, line[i].Z));
        }
        for (int i = 0; i < n; i++) res.Add(line[i]);
        for (int i = n - 1; i >= 0; i--) res.Add(far[i]);
        return Weed(res);
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
