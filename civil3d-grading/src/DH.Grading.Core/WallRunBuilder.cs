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

    /// <summary>[하니스 전용] 토우/크레스트를 표고 대신 링 인덱스로 정하던 옛 동작으로 되돌린다 —
    /// 성토가 뒤집히는 버그를 재현해 S25가 실제로 그걸 잡는 검사인지 확인하는 용도.
    /// 운영 코드에서는 절대 켜지 않는다.</summary>
    public static bool DisableToeCrestOrderForTest;

    /// <summary>[하니스 전용] 토우 정점 끼워넣기를 끈다 — 자체검증(끄면 코너가 현으로 잘려 지표면을 벗어난다)에 쓴다.</summary>
    public static bool DisableToeVertexInsertForTest;

    /// <summary>[하니스 전용] 코너 정점 스냅을 끈다. 이 스냅과 정점 끼워넣기는 **둘 다 코너를 지켜 주므로**,
    /// 하나만 꺼서는 재현이 안 된다 — 자체검증에서는 같이 꺼야 한다.</summary>
    public static bool DisableCornerSnapForTest;

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

        int faceN = 0, skipFlat = 0, skipNoWall = 0, skipShort = 0, bogusCut = 0, skipDegen = 0;
        // [진단 0805] **원본 링의 최대 변**과 **만들어진 옹벽선의 최대 변**을 나란히 남긴다.
        //   둘이 비슷하면 링이 원래 그렇게 생긴 것(전환부 방사형 변 등)이고,
        //   링은 짧은데 옹벽선만 길면 **내 코드가 인접하지 않은 정점을 이었다**는 뜻이다.
        //   JACK '옹벽이 진행방향대로 안 가고 어긋남'의 원인을 이 두 숫자가 가른다.
        double ringSegMax = 0, runSegMax = 0; double runAtX = 0, runAtY = 0;
        // [교차검증] 기하 판정(실제 링 모양)과 구간 규칙의 의도가 어긋난 정점 수.
        //   둘이 크게 갈리면 링이 구간대로 안 만들어졌다는 뜻 — 다음 로그 한 줄로 갈린다.
        int disagree = 0, checkedPts = 0;
        // 불일치를 방향별로 — 구간 밖인데 벽으로 잡힌 자리(=엉뚱한 데 벽이 섬)와 그 반대.
        int extraWall = 0, missWall = 0; double extraWallX = 0, extraWallY = 0; int extraWallB = -1;
        double minRunLen = 0.5;   // 이보다 짧은 조각은 벽으로 세울 수 없다(판넬 한 장도 못 들어감)

        // ★[치명 0805] **연속 쌍을 전부** 돈다(k += 1). 종전엔 `k += 2`로 '링이 [pad, 사면끝, 소단끝, …]
        //   순서로 정확히 짝수 개'라고 가정했는데, 링을 만드는 쪽은 퇴화 링(점 3개 미만)을 **조용히 버린다**.
        //   링 하나가 빠지면 그 뒤 인덱스가 한 칸 밀려 짝이 어긋나고, 그 단부터 **위쪽 옹벽이 통째로 사라진다**
        //   (평탄 쌍만 집게 되어 skipFlat만 늘어난다). 옛 구현(WallPanels·WallBlocks·WallLines)은 전부
        //   연속 쌍을 돌며 '평탄(Z차 0.1m 미만)'만 걸렀다 — 이 가정은 **재작성이 새로 들여온 것**이다.
        //   단 번호도 인덱스가 아니라 **벽면을 만난 횟수**로 센다.
        int bench = -1;
        for (int k = 1; k < rings.Count; k++)
        {
            var rA = rings[k];
            var rB = rings[k - 1];
            if (rA == null || rB == null || rA.Count < 2 || rB.Count < 2) { skipDegen++; continue; }
            double zA = MeanZ(rA), zB = MeanZ(rB);
            double h = System.Math.Abs(zA - zB);
            if (h < 0.1) { skipFlat++; continue; }        // 소단(평탄) 쌍 — 벽면이 아니다
            bench++;                                      // 벽면을 만날 때마다 단이 하나 올라간다
            // ★토우는 **낮은 쪽**, 크레스트는 **높은 쪽**. 링 인덱스로 정하면 안 된다 —
            //   절토는 단이 위로 올라가지만 **성토는 아래로 내려가서** rings[k]가 오히려 밑이다.
            //   인덱스로 고정하면 성토에서 두 선이 뒤바뀌어 ①벽면이 반대쪽을 봐서 무늬·앵커가 흙 속으로 향하고
            //   ②데이라잇 판정('토우가 이미 지반 위인가')이 뒤집힌다.
            //   표고로 정하면 절토·성토가 같은 규칙이 되고, 아래 '크레스트→토우 = 노출면' 규칙도 그대로 성립한다.
            var crest = (zA >= zB) ^ DisableToeCrestOrderForTest ? rA : rB;
            var toe = (zA >= zB) ^ DisableToeCrestOrderForTest ? rB : rA;
            // 이 링에서 실제로 나올 수 있는 최대 변 길이 — 안전망의 기준선(닫는 변 포함).
            double ringMaxSeg = MaxSegOfRing(crest);
            if (ringMaxSeg > ringSegMax) ringSegMax = ringMaxSeg;
            faceN++;

            // ★ '여기가 옹벽인가'는 **기하로 직접 잰다** — 토우↔크레스트 수평 간격이 곧 그 면의 수평 물림이고,
            //   벽이면 minSlope×높이(1:0.05·5m → 0.25m), 사면이면 구배n×높이(1:1.5 → 7.5m)다. 30배 차이라 확실하다.
            //   ※ 호길이 param으로 가르면 **전환부에서 틀린다** — 사면 쪽 링은 바깥으로 크게 부풀어 있어
            //     그 정점을 경계에 투영하면 구간 안으로 들어와 버린다(실측: 사면 정점이 옹벽 구간으로 오분류,
            //     간격 7.5m짜리가 옹벽선에 섞였다). 기하는 지표면이 실제로 어떻게 생겼는지를 그대로 반영한다 —
            //     JACK 요구('최종 지표면의 옹벽선')에도 이쪽이 맞다.
            double wallGap = minSlope * h;
            double gapLim = wallGap * 1.05 + 1e-3;
            double endLim = wallGap * 2.2 + 0.01;   // 마이터 상한(2.0)까지 허용 — 모서리 정점용
            // 이 링의 변 길이 **중앙값**을 기준선으로 — 도면마다 정점 간격이 달라도 스스로 맞춰진다.
            double segLim = System.Math.Max(2.0, MedianSegOfRing(crest) * 4.0);
            // ★[0805 '사선으로 존재하지 않는 옹벽'] 단 링은 **폴리곤 링이라 항상 닫혀 있다** — 다만
            //   첫 점을 끝에 중복해 두는 형식일 수도, 아닐 수도 있다(실측: 중복 **안 함**).
            //   '끝점==첫점일 때만 닫힘'으로 보면 ①닫는 변(마지막→처음)을 통째로 빠뜨려 그 자리 벽이 끊기고
            //   ②세그먼트 수(m-1)와 정점 인덱스(%m)가 어긋나 랩에서 정점 하나를 건너뛴다.
            //   Walk 클래스와 같은 규약으로 통일한다 — 중복 여부만 보고, 닫힘은 항상 참.
            int n = crest.Count;
            bool dup = Dist2D(crest[0], crest[n - 1]) < 1e-6;
            int m = dup ? n - 1 : n;          // 서로 다른 정점 수
            const bool closed = true;         // 링은 언제나 닫혀 있다

            // ★ 판정은 **정점이 아니라 세그먼트 중점**으로 한다.
            //   볼록 모서리를 직각(마이터)으로 만들면 그 **정점만** 바깥으로 더 나가 간격이 커진다
            //   (90° 모서리면 0.25 → 0.25/cos45° = 0.354m). 정점으로 재면 그 한 점이 '벽 아님'으로 떨어져
            //   **모서리마다 벽이 끊기고 코너에 벽이 없어진다**(첫 구현에서 전체 옹벽이 12줄 대신 48줄로 쪼개졌다).
            //   세그먼트 중점은 곧은 구간의 참값(0.25m)을 그대로 주고, 모서리 정점은 양옆 세그먼트가
            //   벽이면 자동으로 포함된다.
            int segN = m;                      // 닫는 변까지 포함 — 링의 실제 변 수
            if (segN < 2) { skipShort++; continue; }
            var segWall = new bool[segN];
            bool any = false;
            for (int s = 0; s < segN; s++)
            {
                var a = crest[s]; var b = crest[(s + 1) % m];
                var mid = new Point3((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
                // 중점은 **엄격히** 벽 간격이어야 하고, 양 끝은 **모서리를 허용하는 느슨한** 한도 안이어야 한다.
                //   양 끝을 안 보면 옹벽↔사면 **전환부의 긴 방사형 변**(실측 7m 이상)이 섞일 수 있고,
                //   그 변 위에 판넬이 균등하게 깔려 '사선으로 존재하지 않는 옹벽'이 된다(JACK 0805 13:44).
                //   느슨한 한도 = 벽 간격 × 마이터 상한(2.0) + 여유 — 직각 모서리 정점(0.354m)은 통과하고
                //   사면 쪽 정점(7.5m)은 확실히 걸린다.
                // ★[0805 계측으로 확정] **변이 너무 길면 벽이 아니다.**
                //   현장 실측: 링 최대변 51.63m / 그중 10.29m가 옹벽선까지 들어와 벽이 엉뚱한 방향으로 뻗었다
                //   (JACK 평면 스샷 '진행방향대로 안 가고 어긋남'). 그 변의 정체는 **옹벽↔사면 전환부의
                //   방사형 변** — 바깥 단일수록 사면이 멀리 나가 수십 m가 된다.
                //   간격 검사만으로는 못 거른다: **토우 링에도 같은 자리에 나란한 방사형 변**이 있어
                //   간격이 0.25m로 측정돼 '벽'으로 통과해 버린다(3점 전부).
                //   벽면 변은 촘촘히 채워진 링에서 오므로 길이가 고르다 → **중앙값의 몇 배를 넘으면 구조적 점프**다.
                segWall[s] = Dist2D(a, b) <= segLim
                          && Dist2D(mid, NearestOn(toe, mid)) <= gapLim
                          && Dist2D(a, NearestOn(toe, a)) <= endLim
                          && Dist2D(b, NearestOn(toe, b)) <= endLim;
                any |= segWall[s];
                // 의도(구간 규칙)와 실제(링 모양)가 어긋나는지 세어 둔다 — 판정에는 쓰지 않는다.
                checkedPts++;
                bool zoneSays = IsWall(GradingGeometry.ParamAt(boundary, cum, mid.X, mid.Y), bench);
                if (segWall[s] != zoneSays)
                {
                    disagree++;
                    // [진단 0805 — JACK '어긋남'] **기하는 벽이라는데 구간은 아니라는** 자리를 따로 센다.
                    //   그런 자리는 사용자가 지정하지 않은 곳에 벽 토막이 서는 것이라 '어긋나게 생성'으로 보인다.
                    //   반대(구간은 벽인데 기하가 아님)는 벽이 안 생기는 쪽이라 증상이 다르다 — 갈라서 센다.
                    if (segWall[s] && !zoneSays)
                    {
                        extraWall++;
                        if (extraWallX == 0) { extraWallX = mid.X; extraWallY = mid.Y; extraWallB = bench; }
                    }
                    else missWall++;
                }
            }
            if (!any) { skipNoWall++; continue; }

            var toePt = new Point3[m];
            // ※[0806] 여기 '전역 최근접'이 오목 코너에서 옆면 크레스트점을 **바닥면 토우로 스냅**시키는 것을
            //   S36으로 확인했다(판넬 하나만 0.25m 다른 선 위 → 옆과 0.30m 벌어짐).
            //   고치려고 '토우를 따라 앞으로만 가는 단조 대응'으로 바꿔 봤더니 **훨씬 나빠졌다**
            //   (진짜 구멍 6→21곳, 하니스 10건 실패) — 크레스트와 토우는 정점 수·시작점·방향이 제각각이라
            //   단순 전진 창(window)으로는 대응이 무너진다. 되돌린다. 올바른 처방은 따로 찾아야 한다.
            int toeHint = -1;   // [0806 성능] 크레스트를 순서대로 훑으면 짝이 되는 토우 구간도 순서대로 움직인다
            var toeSegOf = new int[m];
            for (int i = 0; i < m; i++)
            {
                toeSegOf[i] = toeHint;
                // 이 크레스트 정점이 향하는 방향(앞뒤 이웃으로 잡는다) — 짝이 될 토우 변은 이 방향과 나란해야 한다.
                var pv = crest[((i - 1) % m + m) % m]; var nx2 = crest[(i + 1) % m];
                double dxc = nx2.X - pv.X, dyc = nx2.Y - pv.Y;
                double lc2 = System.Math.Sqrt(dxc * dxc + dyc * dyc);
                if (lc2 < 1e-9) { toePt[i] = NearestOn(toe, crest[i]); continue; }
                toePt[i] = NearestOnAligned(toe, crest[i], dxc / lc2, dyc / lc2, wallGap * 4 + 0.2, ref toeHint);
                toeSegOf[i] = toeHint;
            }

            // ★[JACK 0806 '절토·성토 구분해서 방향 맞춰라' — 성토 실측 0.251m] 크레스트가 꺾이는 자리는
            //   토우도 꺾인다. 그 짝은 **토우의 코너 정점**이어야 하는데, 구간 위 최근접점으로 잡으면
            //   마이터 코너에서 **옆 변의 한 점**에 붙는다 — 90° 마이터면 토우 코너까지는 0.354m인데
            //   양옆 변까지는 0.25m라 그쪽이 더 가깝기 때문이다. 그러면 그 판넬이 이웃 벽면의 선 위에 놓여
            //   **한 선(0.25m = 토우↔크레스트 간격)만큼 밀린다**(성토 바깥 마이터에서 0.251m 실측).
            //   절토에서 안 보였던 건 그쪽 마이터가 안쪽으로 접혀 코너가 더 가까웠기 때문이고,
            //   규칙 자체는 절·성토 공용이라 여기서 한 번에 고친다.
            if (!DisableCornerSnapForTest)
            {
                double cosCorner = System.Math.Cos(12.0 * System.Math.PI / 180.0);
                double snapLim = wallGap * 4 + 0.2;
                for (int i = 0; i < m; i++)
                {
                    var pv = crest[((i - 1) % m + m) % m]; var nx2 = crest[(i + 1) % m];
                    double axc = crest[i].X - pv.X, ayc = crest[i].Y - pv.Y;
                    double bxc = nx2.X - crest[i].X, byc = nx2.Y - crest[i].Y;
                    double lac = System.Math.Sqrt(axc * axc + ayc * ayc), lbc = System.Math.Sqrt(bxc * bxc + byc * byc);
                    if (lac < 1e-9 || lbc < 1e-9) continue;
                    if ((axc * bxc + ayc * byc) / (lac * lbc) >= cosCorner) continue;   // 꺾이는 자리가 아니다
                    // 토우 **정점** 중 가장 가까운 것으로 — 구간 위의 점이 아니라.
                    // [0806 성능] 제곱거리로 비교하고(√ 생략) 경계상자로 먼저 거른다 — 링이 수천 점이라
                    //   전수 대조하면 옹벽변환이 눈에 띄게 느려진다(JACK 0806 지적).
                    int bi = -1; double bd2 = double.MaxValue, lim2 = snapLim * snapLim;
                    double qx0 = crest[i].X - snapLim, qx1 = crest[i].X + snapLim;
                    double qy0 = crest[i].Y - snapLim, qy1 = crest[i].Y + snapLim;
                    // [0806 성능] 짝이 되는 토우 구간은 위에서 이미 찾아 뒀다 — 그 둘레 몇 점만 보면 된다.
                    //   전수 대조하면 성토 링(수천 점)에서 코너마다 수천 번을 돌아 옹벽변환이 멈춘 듯 느려진다.
                    int qs = 0, qe = toe.Count - 1;
                    if (toe.Count > 256 && toeSegOf[i] >= 0)
                    { qs = System.Math.Max(0, toeSegOf[i] - 8); qe = System.Math.Min(toe.Count - 1, toeSegOf[i] + 9); }
                    for (int q = qs; q <= qe; q++)
                    {
                        if (toe[q].X < qx0 || toe[q].X > qx1 || toe[q].Y < qy0 || toe[q].Y > qy1) continue;
                        double ddx = toe[q].X - crest[i].X, ddy = toe[q].Y - crest[i].Y;
                        double d2 = ddx * ddx + ddy * ddy;
                        if (d2 < bd2) { bd2 = d2; bi = q; }
                    }
                    if (bi >= 0 && bd2 <= lim2) toePt[i] = toe[bi];
                }
            }

            foreach (var seg in SegRunsToVertexRuns(segWall, closed, m))
            {
                var cr = new List<Point3>(seg.Count);
                var to = new List<Point3>(seg.Count);
                foreach (var idx in seg) { cr.Add(crest[idx]); to.Add(toePt[idx]); }
                if (cr.Count < 2) { skipShort++; continue; }
                // ★[JACK 0806 '토우선이 일정 간격으로 정점이 찍히면서 만들어지는 것 같은데 지표면을 정확히
                //   따라가는 방식으로 뽑을 수 없나' — 스샷에서 토우선 코너가 지표면 단 모서리와 어긋남]
                //   맞다. 표본을 **크레스트 정점에서만** 뽑아 왔다 — 크레스트는 1m 간격으로 촘촘하지만
                //   토우의 **진짜 코너 정점**은 그 사이에 떨어지므로 표본에 안 들어가고, 그 자리가
                //   현(弦)으로 잘려 **지표면 모서리를 벗어난다**. 토우 링의 실제 정점을 끼워 넣어
                //   선이 지표면(그 링으로 만든 면)을 정확히 따라가게 한다.
                if (!DisableToeVertexInsertForTest) InsertToeVertices(cr, to, toe);
                double len = 0;
                for (int i = 0; i + 1 < cr.Count; i++) len += Dist2D(cr[i], cr[i + 1]);
                if (len < minRunLen) { skipShort++; continue; }

                // ★안전망 — 옹벽선의 어떤 변도 원본 링의 최대 변보다 크게 길 수 없다.
                //   길다면 인접하지 않은 정점이 이어진 것(가짜 선분)이고, 판넬은 균등 분할이라
                //   그 위에 **일정한 사슬**로 깔려 '사선으로 존재하지 않는 옹벽'이 된다(JACK 0805 13:44).
                //   원인이 무엇이든 도면까지 나가지 않게 여기서 끊고 사유별로 센다.
                foreach (var piece in SplitAtBogusSeg(cr, to, ringMaxSeg, ref bogusCut))
                {
                    double pl = 0;
                    for (int i = 0; i + 1 < piece.Cr.Count; i++) pl += Dist2D(piece.Cr[i], piece.Cr[i + 1]);
                    if (pl < minRunLen) { skipShort++; continue; }
                    for (int q = 0; q + 1 < piece.Cr.Count; q++)
                    {
                        double sl = Dist2D(piece.Cr[q], piece.Cr[q + 1]);
                        if (sl > runSegMax) { runSegMax = sl; runAtX = piece.Cr[q].X; runAtY = piece.Cr[q].Y; }
                    }
                    outp.Add(new WallRun { Up = up, Bench = bench, Toe = piece.To, Crest = piece.Cr, Height = h });
                }
            }
        }

        LastDiag = $"옹벽선 {outp.Count}줄 · 링 {rings.Count}개 · 벽면쌍 {faceN}(기대 {rings.Count / 2})" +
                   $" · 건너뜀(평탄 {skipFlat} · 퇴화 {skipDegen} · 옹벽아님 {skipNoWall} · 짧음 {skipShort})" +
                   $" · 전역 1:{globalSlope}{(globalIsWall ? "(수직)" : "")} · 구간 {(zones?.Count ?? 0)}개" +
                   $" · 기하↔구간 불일치 {disagree}/{checkedPts}점" +
                   (extraWall > 0 ? $"(⚠구간 밖인데 벽 {extraWall}점 — 첫 자리 {extraWallX:F0},{extraWallY:F0} {extraWallB + 1}단 · 구간 안인데 벽 아님 {missWall}점)"
                                  : missWall > 0 ? $"(구간 안인데 벽 아님 {missWall}점)" : "") +
                   (bogusCut > 0 ? $" · ⚠가짜 긴 변에서 끊음 {bogusCut}곳(사선 옹벽 차단)" : "") +
                   $" · 변길이 링 {ringSegMax:F2}m / 옹벽선 {runSegMax:F2}m @ {runAtX:F0},{runAtY:F0}" +
                   (runSegMax > ringSegMax + 1e-6 ? " ⚠옹벽선이 링보다 길다 = 인접하지 않은 정점이 이어짐" : "");
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

    /// <summary>링의 변 길이 중앙값(닫는 변 포함) — 이 링의 '보통 변 길이'. 전환부의 긴 변에 안 흔들린다.</summary>
    private static double MedianSegOfRing(IReadOnlyList<Point3> r)
    {
        int c = r.Count;
        if (c < 2) return 0;
        var l = new List<double>(c);
        for (int i = 0; i < c; i++) l.Add(Dist2D(r[i], r[(i + 1) % c]));
        l.Sort();
        return l[l.Count / 2];
    }

    /// <summary>링의 최대 변 길이(닫는 변 포함).</summary>
    private static double MaxSegOfRing(IReadOnlyList<Point3> r)
    {
        double mx = 0;
        int c = r.Count;
        for (int i = 0; i < c; i++) mx = System.Math.Max(mx, Dist2D(r[i], r[(i + 1) % c]));
        return mx;
    }

    /// <summary>옹벽선을 '가짜로 긴 변'에서 끊는다 — 인접하지 않은 정점이 이어진 자리.
    /// 기준은 원본 링의 최대 변 × 1.5 + 0.5m(정상 변은 절대 이보다 길 수 없다).</summary>
    private static List<(List<Point3> Cr, List<Point3> To)> SplitAtBogusSeg(
        List<Point3> cr, List<Point3> to, double ringMaxSeg, ref int bogusCut)
    {
        var res = new List<(List<Point3>, List<Point3>)>();
        double lim = ringMaxSeg * 1.5 + 0.5;
        int s = 0;
        for (int i = 0; i + 1 < cr.Count; i++)
        {
            if (Dist2D(cr[i], cr[i + 1]) <= lim) continue;
            bogusCut++;
            if (i >= s) res.Add((cr.GetRange(s, i - s + 1), to.GetRange(s, i - s + 1)));
            s = i + 1;
        }
        if (s < cr.Count) res.Add((cr.GetRange(s, cr.Count - s), to.GetRange(s, cr.Count - s)));
        return res;
    }

    /// <summary>
    /// [0805 '사선으로 존재하지 않는 옹벽' — 소비 시점 안전망] 저장된 옹벽선에서 <b>비정상적으로 긴 변</b>을
    /// 찾아 거기서 선을 끊는다.
    /// <para>
    /// 단 링은 1m 이하로 촘촘히 채워지므로(GradingGeometry densify ≤ 1.0m) 옹벽선의 변도 그 정도여야 한다.
    /// 그보다 몇 배 긴 변은 인접하지 않은 정점이 이어진 것이고, 판넬은 균등 분할이라 그 위에
    /// <b>일정한 사슬</b>로 깔려 부지를 가로지르는 가짜 옹벽이 된다(현장 실측: 44.55m 변 1개).
    /// </para>
    /// 기준은 <b>전체 옹벽선의 변 길이 중앙값</b>으로 잡는다 — 도면마다 정점 간격이 달라도 스스로 맞춰진다.
    /// 옛 버전이 만들어 이미 저장된 선도 여기서 걸러지므로, 정지면을 다시 만들지 않아도 도면이 깨끗해진다.
    /// </summary>
    public static List<WallRun> SplitLongSegments(IReadOnlyList<WallRun>? runs, out string diag, double minLen = 0.5)
    {
        var outp = new List<WallRun>();
        diag = "";
        if (runs == null || runs.Count == 0) return outp;

        var lens = new List<double>();
        foreach (var r in runs)
            for (int i = 0; i + 1 < r.Crest.Count; i++) lens.Add(Dist2D(r.Crest[i], r.Crest[i + 1]));
        if (lens.Count == 0) { outp.AddRange(runs); return outp; }
        lens.Sort();
        double median = lens[lens.Count / 2];
        double lim = System.Math.Max(2.5, median * 5.0);

        int cutN = 0, droppedN = 0; double worst = 0, atX = 0, atY = 0;
        foreach (var r in runs)
        {
            int n = System.Math.Min(r.Crest.Count, r.Toe.Count);
            int s = 0;
            for (int i = 0; i + 1 < n; i++)
            {
                double d = Dist2D(r.Crest[i], r.Crest[i + 1]);
                if (d <= lim) continue;
                cutN++;
                if (d > worst) { worst = d; atX = r.Crest[i].X; atY = r.Crest[i].Y; }
                Emit(r, s, i, outp, minLen, ref droppedN);
                s = i + 1;
            }
            Emit(r, s, n - 1, outp, minLen, ref droppedN);
        }
        diag = cutN == 0
            ? $"옹벽선 검사 — 이상 없음(변 중앙값 {median:F2}m · 한도 {lim:F2}m · {runs.Count}줄)"
            : $"⚠옹벽선 가짜 변 {cutN}곳에서 끊음(최대 {worst:F1}m @ {atX:F0},{atY:F0} · 한도 {lim:F2}m)" +
              $" · {runs.Count}줄 → {outp.Count}줄(짧아서 버림 {droppedN})";
        return outp;
    }

    /// <summary>
    /// [0805 JACK '누락됨' — 벽 중간이 한 칸 비는 것] 같은 단에서 <b>끝이 맞닿는 옹벽선 조각들을 다시 잇는다</b>.
    /// <para>
    /// 옹벽선이 여러 조각으로 갈리는 이유는 여러 가지다(링 이음매를 못 보고 끊긴 옛 결함, 뒤 구역 잘라내기 등).
    /// 조각마다 따로 판넬을 깔면 그 사이가 한 칸 빈 것처럼 보인다 — 실제로는 이어진 한 벽이다.
    /// 끝점이 <paramref name="tol"/> 안에서 맞닿으면 한 줄로 합쳐, 판넬이 그 위를 연속으로 덮게 한다.
    /// </para>
    /// </summary>
    /// <param name="tol">이 거리 안에서 끝이 맞닿아야 잇는다.
    /// ※[JACK 0805 '커브쪽에 한 판넬만 안쪽으로 생성'] 처음엔 1.5m로 잡았는데, **커브에서 1.5m를 직선으로
    /// 이으면 그 선이 안쪽으로 파고들고** 그 위에 판넬 한 장이 통째로 안쪽에 놓인다(90° 코너를 1.5m로
    /// 가로지르면 1m 넘게 파고든다). 실제로 이어야 할 조각은 **정점을 공유하거나 정점 간격 이내**라
    /// 0.35m면 충분하다. 잇는 다리 길이는 아래 diag로 남겨 부족하면 로그로 드러나게 한다.</param>
    public static List<WallRun> MergeAdjacent(IReadOnlyList<WallRun>? runs, out int mergedN, double tol = 0.35)
    {
        mergedN = 0;
        LastBridge = "";
        double bridgeMax = 0, bridgeX = 0, bridgeY = 0;
        var outp = new List<WallRun>();
        if (runs == null || runs.Count == 0) return outp;
        var pool = new List<WallRun>(runs);
        var used = new bool[pool.Count];

        for (int i = 0; i < pool.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var cr = new List<Point3>(pool[i].Crest);
            var to = new List<Point3>(pool[i].Toe);
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int j = 0; j < pool.Count; j++)
                {
                    if (used[j]) continue;
                    var o = pool[j];
                    if (o.Up != pool[i].Up || o.Bench != pool[i].Bench) continue;
                    if (System.Math.Abs(o.Height - pool[i].Height) > 0.05) continue;
                    if (o.Crest.Count < 2) continue;

                    // 다리 길이를 기록해 둔다 — 길면 커브를 가로질러 안쪽으로 파고든다.
                    void Bridge(double d, Point3 at)
                    { if (d > bridgeMax) { bridgeMax = d; bridgeX = at.X; bridgeY = at.Y; } }

                    // 내 끝 ↔ 상대 앞
                    if (Dist2D(cr[cr.Count - 1], o.Crest[0]) <= tol)
                    { Bridge(Dist2D(cr[cr.Count - 1], o.Crest[0]), o.Crest[0]); cr.AddRange(o.Crest); to.AddRange(o.Toe); }
                    // 내 끝 ↔ 상대 뒤(상대를 뒤집어 붙임)
                    else if (Dist2D(cr[cr.Count - 1], o.Crest[o.Crest.Count - 1]) <= tol)
                    {
                        Bridge(Dist2D(cr[cr.Count - 1], o.Crest[o.Crest.Count - 1]), o.Crest[o.Crest.Count - 1]);
                        for (int k = o.Crest.Count - 1; k >= 0; k--) { cr.Add(o.Crest[k]); to.Add(o.Toe[k]); }
                    }
                    // 내 앞 ↔ 상대 뒤
                    else if (Dist2D(cr[0], o.Crest[o.Crest.Count - 1]) <= tol)
                    { Bridge(Dist2D(cr[0], o.Crest[o.Crest.Count - 1]), cr[0]); cr.InsertRange(0, o.Crest); to.InsertRange(0, o.Toe); }
                    // 내 앞 ↔ 상대 앞(상대를 뒤집어 앞에 붙임)
                    else if (Dist2D(cr[0], o.Crest[0]) <= tol)
                    {
                        Bridge(Dist2D(cr[0], o.Crest[0]), cr[0]);
                        for (int k = 0; k < o.Crest.Count; k++) { cr.Insert(0, o.Crest[k]); to.Insert(0, o.Toe[k]); }
                    }
                    else continue;
                    used[j] = true; mergedN++; grew = true;
                }
            }
            outp.Add(new WallRun { Up = pool[i].Up, Bench = pool[i].Bench, Height = pool[i].Height, Toe = to, Crest = cr });
        }
        LastBridge = mergedN == 0 ? ""
            : $"이어붙인 다리 최대 {bridgeMax:F2}m @ {bridgeX:F0},{bridgeY:F0}(한도 {tol:F2}m)";
        return outp;
    }

    /// <summary>직전 <see cref="MergeAdjacent"/>가 이어붙인 '다리'의 최대 길이와 자리 — 길수록 커브를 가로질러
    /// 안쪽으로 파고든다(JACK 0805 '커브쪽에 한 판넬만 안쪽으로 생성').</summary>
    public static string LastBridge { get; private set; } = "";

    private static void Emit(WallRun r, int s, int e, List<WallRun> outp, double minLen, ref int dropped)
    {
        if (e <= s) return;
        var cr = r.Crest.GetRange(s, e - s + 1);
        var to = r.Toe.GetRange(s, e - s + 1);
        double len = 0;
        for (int i = 0; i + 1 < cr.Count; i++) len += Dist2D(cr[i], cr[i + 1]);
        if (len < minLen) { dropped++; return; }
        outp.Add(new WallRun { Up = r.Up, Bench = r.Bench, Height = r.Height, Toe = to, Crest = cr });
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

    /// <summary>링 위에서 점 q에 가장 가까운 점(2D 최근접, Z는 그 변에서 보간).
    /// 링은 닫혀 있으므로 **닫는 변(마지막→처음)도 함께 본다** — 빠뜨리면 그 근처에서 엉뚱한 점을 집는다.</summary>
    /// <summary>
    /// ★[0806 JACK '오목부마다 누락에 선형도 어긋남' — S36으로 자리 확정] 크레스트 정점의 짝이 될 토우점을
    /// <b>나란한 변에서만</b> 찾는다.
    /// <para>
    /// 종전 <see cref="NearestOn"/>은 토우 <b>전체</b>에서 최근접점을 골랐다. 곧은 구간에선 옳지만
    /// <b>오목 코너에서는 옆면의 크레스트 정점이 바닥면 토우로 스냅한다</b> — 크레스트가 토우보다
    /// d(=구배n×단높이=0.25m) 안쪽이라 코너 근처에선 옆면보다 <b>수직으로 만나는 바닥면이 더 가깝기</b> 때문이다.
    /// 그러면 그 판넬 하나만 이웃과 다른 선 위에 놓이고(선형 어긋남 0.25m), 그만큼 옆과 벌어진다(구멍 0.30m).
    /// </para>
    /// 벽면은 토우와 크레스트가 <b>나란한 한 쌍</b>이므로, 크레스트의 진행 방향과 50° 넘게 어긋난 토우 변은
    /// 애초에 짝이 될 수 없다 — 그 변들을 후보에서 빼면 스냅이 원천적으로 안 생긴다.
    /// 나란한 후보가 하나도 없으면(드문 형상) 종전대로 전역 최근접으로 물러난다.
    /// <para>
    /// ※'토우를 따라 앞으로만 가는 단조 대응'도 시도했으나 <b>훨씬 나빠졌다</b>(구멍 6→21곳) —
    /// 두 선은 정점 수·시작점·방향이 제각각이라 전진 창으로는 대응이 무너진다. 다시 시도하지 말 것.
    /// </para></summary>
    /// <summary>
    /// ★[JACK 0806] 옹벽선이 <b>지표면을 정확히 따라가게</b> — 토우 링의 실제 정점을 표본에 끼워 넣는다.
    /// <para>
    /// 표본은 크레스트 정점에서만 뽑는다(그래야 토우↔크레스트가 인덱스 1:1로 유지된다 — 치명-4).
    /// 그런데 크레스트는 1m 간격으로 촘촘한 반면 <b>토우의 코너 정점</b>은 그 표본 사이에 떨어져
    /// 선에 안 들어간다 → 코너가 현(弦)으로 잘려 <b>지표면의 단 모서리를 벗어난다</b>(JACK 스샷).
    /// </para>
    /// 토우 링 정점 중 이 구간의 토우 경로 위에 있고 아직 표본이 아닌 것을 찾아, 그 자리에
    /// (토우=그 정점, 크레스트=양옆 표본의 같은 비율 보간점) 한 쌍을 끼운다.
    /// 인덱스 1:1은 그대로 유지되고, 코너는 잘리지 않는다.
    /// </summary>
    internal static void InsertToeVertices(List<Point3> cr, List<Point3> to, IReadOnlyList<Point3> toeRing)
    {
        if (cr == null || to == null || toeRing == null || cr.Count != to.Count || cr.Count < 2) return;
        // ★이 값이 너무 작으면 **정작 끼워야 할 정점이 걸러진다**. 코너 정점은 현(弦)에서 떨어져 있는 게
        //   당연하고(그 떨어진 양이 바로 잘린 깊이 — 실측 0.12m), 0.05로 잡으면 그게 전부 '내 경로 아님'으로
        //   빠져 끼워넣기가 **아무 일도 안 한다**(첫 시도가 그랬다). 다른 단 링(소단 폭 1m)만 배제하면 되므로 넉넉히.
        const double onPath = 0.5;      // 이 구간의 토우 경로 위라고 볼 거리(m)
        const double already = 0.02;    // 이미 표본인 정점으로 볼 거리(m)
        // 끼울 것들을 (구간 k, 그 안의 비율 t, 토우 정점) 으로 모은 뒤 뒤에서부터 삽입한다.
        // ★[0806 성능] 링은 수천 점이고 `to`도 수백 점이라 전수 대조는 **정점수의 제곱**이 된다
        //   (JACK 0806 '옹벽변환도 시간이 늘어난 것 같은데'). 값은 그대로 두고 **먼저 싸게 걸러낸다** —
        //   ①이 구간의 경계상자 밖 정점은 즉시 버리고 ②구간별로도 경계상자로 먼저 거른다.
        double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
        foreach (var q in to)
        { bx0 = System.Math.Min(bx0, q.X); by0 = System.Math.Min(by0, q.Y); bx1 = System.Math.Max(bx1, q.X); by1 = System.Math.Max(by1, q.Y); }
        bx0 -= onPath; by0 -= onPath; bx1 += onPath; by1 += onPath;

        var ins = new List<(int K, double T, Point3 V)>();
        // [0806 성능] 링과 `to`는 같은 선을 같은 순서로 훑으므로, 직전에 맞은 구간 둘레만 봐도 거의 항상 맞는다.
        //   창에서 못 찾으면 전수로 물러나므로 결과는 창 없이 돌린 것과 같다.
        int hint = -1; const int win = 48;
        foreach (var v in toeRing)
        {
            if (v.X < bx0 || v.X > bx1 || v.Y < by0 || v.Y > by1) continue;   // 이 구간 근처가 아니다
            int bk = -1; double bt = 0, bd = double.MaxValue;
            int lo = (hint >= 0 && to.Count > 256) ? System.Math.Max(0, hint - win) : 0;
            int hi = (hint >= 0 && to.Count > 256) ? System.Math.Min(to.Count - 2, hint + win) : to.Count - 2;
            for (int pass = 0; pass < 2; pass++)
            {
            for (int k = lo; k <= hi; k++)
            {
                // 구간 경계상자로 싼 거절 — 이게 대부분을 걸러 안쪽 계산까지 안 간다.
                double sx0 = System.Math.Min(to[k].X, to[k + 1].X) - onPath, sx1 = System.Math.Max(to[k].X, to[k + 1].X) + onPath;
                double sy0 = System.Math.Min(to[k].Y, to[k + 1].Y) - onPath, sy1 = System.Math.Max(to[k].Y, to[k + 1].Y) + onPath;
                if (v.X < sx0 || v.X > sx1 || v.Y < sy0 || v.Y > sy1) continue;
                double dx = to[k + 1].X - to[k].X, dy = to[k + 1].Y - to[k].Y, L2 = dx * dx + dy * dy;
                if (L2 < 1e-12) continue;
                double t = System.Math.Clamp(((v.X - to[k].X) * dx + (v.Y - to[k].Y) * dy) / L2, 0, 1);
                double px = to[k].X + dx * t, py = to[k].Y + dy * t;
                double d = System.Math.Sqrt((v.X - px) * (v.X - px) + (v.Y - py) * (v.Y - py));
                if (d < bd) { bd = d; bk = k; bt = t; }
            }
            if (bd <= onPath) break;                                    // 창 안에서 찾았다
            if (lo == 0 && hi == to.Count - 2) break;                   // 이미 전수였다
            lo = 0; hi = to.Count - 2;                                  // 못 찾았으면 전수로 물러난다
            }
            if (bk >= 0) hint = bk;
            if (bk < 0 || bd > onPath) continue;                        // 이 구간의 토우가 아니다
            if (Dist2D(v, to[bk]) < already || Dist2D(v, to[bk + 1]) < already) continue;   // 이미 표본
            ins.Add((bk, bt, v));
        }
        if (ins.Count == 0) return;
        ins.Sort((a, b) => a.K != b.K ? b.K.CompareTo(a.K) : b.T.CompareTo(a.T));   // 뒤에서부터
        foreach (var (k, t, v) in ins)
        {
            var c0 = cr[k]; var c1 = cr[k + 1];
            cr.Insert(k + 1, new Point3(c0.X + (c1.X - c0.X) * t, c0.Y + (c1.Y - c0.Y) * t, c0.Z + (c1.Z - c0.Z) * t));
            to.Insert(k + 1, v);
        }
    }

    /// <summary>[0806 성능] 위와 같되 <b>직전에 맞은 구간 번호(hint)</b> 둘레부터 찾는다.
    /// 크레스트를 순서대로 훑으면 짝이 되는 토우 구간도 순서대로 움직이므로, 창(window) 안에서 거의 항상 맞는다.
    /// 창에서 못 찾으면 전수 탐색으로 물러나므로 **결과는 창 없이 돌린 것과 같다**(성토 링은 수천 점이라
    /// 전수 탐색이 정점수의 제곱이 되어 내보내기가 멈춘 것처럼 느려진다 — JACK 0806).</summary>
    internal static Point3 NearestOnAligned(IReadOnlyList<Point3> line, Point3 q, double dirX, double dirY,
                                            double maxDist, ref int hint)
    {
        int cnt0 = line.Count;
        bool dup0 = cnt0 >= 2 && Dist2D(line[0], line[cnt0 - 1]) < 1e-9;
        int segs0 = dup0 ? cnt0 - 1 : cnt0;
        if (hint >= 0 && segs0 > 256)
        {
            const int win = 48;
            double best0 = double.MaxValue; Point3 bp0 = default; bool got0 = false; int bi0 = hint;
            double cosLim0 = 0.643;
            for (int k = hint - win; k <= hint + win; k++)
            {
                int i = ((k % segs0) + segs0) % segs0;
                var a = line[i]; var b = line[(i + 1) % cnt0];
                double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy;
                if (L2 < 1e-12) continue;
                double L = System.Math.Sqrt(L2);
                if (System.Math.Abs((dx * dirX + dy * dirY) / L) < cosLim0) continue;
                double t = System.Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2, 0, 1);
                double px = a.X + dx * t, py = a.Y + dy * t;
                double d = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
                if (d > maxDist * maxDist) continue;
                if (d < best0) { best0 = d; bp0 = new Point3(px, py, a.Z + (b.Z - a.Z) * t); got0 = true; bi0 = i; }
            }
            // 창 안에서 **상한 안쪽**에 짝을 찾았으면 그게 최근접이다(창 밖은 더 멀다 — 선이 순서대로 가므로).
            if (got0) { hint = bi0; return bp0; }
        }
        var r = NearestOnAlignedFull(line, q, dirX, dirY, maxDist, out int found);
        if (found >= 0) hint = found;
        return r;
    }

    internal static Point3 NearestOnAligned(IReadOnlyList<Point3> line, Point3 q, double dirX, double dirY,
                                            double maxDist)
    { int h = -1; return NearestOnAligned(line, q, dirX, dirY, maxDist, ref h); }

    private static Point3 NearestOnAlignedFull(IReadOnlyList<Point3> line, Point3 q, double dirX, double dirY,
                                               double maxDist, out int bestSeg)
    {
        bestSeg = -1;
        const double cosLim = 0.643;                    // cos 50°
        // ★거리 상한이 없으면 **부지 반대편의 평행한 변**에 붙는다 — 실측 22.56m짜리 가짜 옹벽선이 나왔다(S25).
        //   벽면의 토우는 크레스트에서 수평으로 구배n×단높이(0.25m)쯤에 있으므로, 그 몇 배를 넘으면 짝이 아니다.
        double maxD2 = maxDist * maxDist;
        double best = double.MaxValue; Point3 bp = default; bool got = false;
        int cnt = line.Count;
        bool dupEnd = cnt >= 2 && Dist2D(line[0], line[cnt - 1]) < 1e-9;
        int segs = dupEnd ? cnt - 1 : cnt;
        // [0806 성능] 거리 상한이 곧 탐색 반경이다 — 경계상자로 먼저 거르면 링이 수천 점이어도 싸다.
        double qx0 = q.X - maxDist, qx1 = q.X + maxDist, qy0 = q.Y - maxDist, qy1 = q.Y + maxDist;
        for (int i = 0; i < segs; i++)
        {
            var a = line[i]; var b = line[(i + 1) % cnt];
            if ((a.X < qx0 && b.X < qx0) || (a.X > qx1 && b.X > qx1) ||
                (a.Y < qy0 && b.Y < qy0) || (a.Y > qy1 && b.Y > qy1)) continue;
            double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy;
            if (L2 < 1e-12) continue;
            double L = System.Math.Sqrt(L2);
            if (System.Math.Abs((dx * dirX + dy * dirY) / L) < cosLim) continue;   // 나란하지 않은 변 — 짝이 아니다
            double t = System.Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2, 0, 1);
            double px = a.X + dx * t, py = a.Y + dy * t;
            double d = (q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py);
            if (d > maxD2) continue;                                               // 너무 멀다 — 짝이 아니다
            if (d < best) { best = d; bp = new Point3(px, py, a.Z + (b.Z - a.Z) * t); got = true; bestSeg = i; }
        }
        return got ? bp : NearestOn(line, q);
    }

    internal static Point3 NearestOn(IReadOnlyList<Point3> line, Point3 q)
    {
        double best = double.MaxValue;
        Point3 bp = line[0];
        int cnt = line.Count;
        bool dupEnd = cnt >= 2 && Dist2D(line[0], line[cnt - 1]) < 1e-9;
        int segs = dupEnd ? cnt - 1 : cnt;      // 중복 없으면 닫는 변 한 개가 더 있다
        for (int i = 0; i < segs; i++)
        {
            var a = line[i]; var b = line[(i + 1) % cnt];
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
