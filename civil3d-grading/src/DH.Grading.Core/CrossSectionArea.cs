namespace DH.Grading.Core;

/// <summary>★★[JACK 0826] <b>횡단면 한 장의 면적</b>을 잰다 — 수량표의 알맹이다.
///
/// <para>횡단 수량은 결국 <b>두 선 사이의 넓이</b>다. 절토는 원지반이 계획면보다 높은 만큼,
/// 성토는 그 반대, 터파기는 지표와 굴착 바닥 사이. 전부 같은 셈이라 함수 하나로 푼다.</para>
///
/// <para><b>왜 Core인가.</b> 도면이 없어도 되는 순수 산수이고, 수량은 <b>돈이 걸린 숫자</b>라
/// 눈으로 확인하는 것으로는 부족하다. 하니스가 손으로 푼 답과 맞대 볼 수 있어야 한다.</para>
///
/// <para><b>교차점을 반드시 잡는다.</b> 한 단면에 절토와 성토가 <b>같이</b> 있는 것이 보통이다
/// (한쪽은 깎고 한쪽은 쌓는다). 두 선이 만나는 자리에서 갈라 주지 않으면 절토에 성토가 섞여
/// 둘 다 틀린다 — 서로 상쇄돼 합계는 그럴듯해 보이므로 <b>더 위험하다</b>.</para></summary>
public static class CrossSectionArea
{
    /// <summary>두 선 사이에서 <b>위 선이 아래 선보다 높은 구간만</b>의 넓이(㎡).
    ///
    /// <para>사다리꼴로 잰다. 두 선이 교차하면 그 자리를 <b>선형 보간으로 찾아 끊는다</b> —
    /// 끊지 않고 한 칸을 통째로 세면 부호가 섞여 값이 뭉개진다.</para></summary>
    /// <param name="x">가로 위치(중심에서의 거리, m). 오름차순이어야 한다.</param>
    /// <param name="top">위 선의 표고(m).</param>
    /// <param name="bot">아래 선의 표고(m).</param>
    /// <returns>넓이(㎡). 위 선이 줄곧 아래면 0.</returns>
    public static double Above(double[] x, double[] top, double[] bot)
    {
        // ★★[검토] <b>잰 칸이 하나도 없으면 0이 아니라 NaN이다.</b>
        //   0은 "재 봤더니 없다", NaN은 "잴 수 없었다" — 이 파일이 스스로 세운 규칙인데
        //   여기서 깨지고 있었다. 지표면 밖이라 전부 NaN인 단면이 <b>0.00</b>으로 표에 찍혔고,
        //   로그도 "뺐다"로 세어 <b>성공처럼 보였다</b>.
        if (x == null || top == null || bot == null) return double.NaN;
        int n = System.Math.Min(x.Length, System.Math.Min(top.Length, bot.Length));
        if (n < 2) return double.NaN;

        double area = 0;
        bool sawAny = false;      // 한 칸이라도 실제로 쟀는가
        for (int i = 0; i < n - 1; i++)
        {
            double x0 = x[i], x1 = x[i + 1];
            double w = x1 - x0;
            if (!(w > 0)) continue;
            double d0 = top[i] - bot[i], d1 = top[i + 1] - bot[i + 1];
            if (double.IsNaN(d0) || double.IsNaN(d1)) continue;

            sawAny = true;                     // 두 값이 다 성하니 이 칸은 <b>쟀다</b>
            if (d0 >= 0 && d1 >= 0)            // 한 칸이 통째로 위
                area += (d0 + d1) / 2.0 * w;
            else if (d0 <= 0 && d1 <= 0)       // 통째로 아래 — 셀 것이 없다(그래도 잰 것이다)
                continue;
            else
            {
                // ★교차 — 부호가 바뀌는 자리를 찾아 <b>위쪽 삼각형만</b> 센다.
                double t = d0 / (d0 - d1);     // 0~1 사이. d0-d1은 부호가 갈리므로 0이 아니다.
                double xc = x0 + w * t;
                if (d0 > 0) area += d0 / 2.0 * (xc - x0);
                else area += d1 / 2.0 * (x1 - xc);
            }
        }
        return sawAny ? area : double.NaN;
    }

    /// <summary>같은 두 선에서 <b>깊이가 한계를 넘는 구간</b>과 <b>넘지 않는 구간</b>으로 갈라 잰다.
    ///
    /// <para>터파기 수량표가 <c>5.0m 이하</c>와 <c>5.0m 이상</c>을 나누는 것이 이것이다.
    /// 깊이는 <b>그 자리의 위–아래 차</b>이고, 한계선을 넘는 부분만 '이상'으로 간다 —
    /// 단면 전체를 깊이 최대값으로 한쪽에 몰아넣으면 얕은 가장자리까지 비싼 품이 매겨진다.</para>
    ///
    /// <para><b>가르는 자리도 보간으로 찾는다.</b> 깊이가 정확히 한계인 지점에서 끊어야
    /// 두 값의 합이 전체와 같아진다(하니스가 그것을 검사한다).</para>
    ///
    /// <para>★★★<b>경계선은 지표와 나란히 간다</b>(JACK 0826 확정). 두 방식이 있다:
    /// <list type="bullet">
    /// <item><b>지표평행</b> — 경계선이 지표를 따라 기운다. <b>이것을 쓴다.</b></item>
    /// <item>수평면 — 측점마다 대표 깊이를 정하고 수평으로 자른다.</item>
    /// </list>
    /// 계획면이 평탄한 절토부에서는 두 방식이 <b>같다</b>. 갈리는 것은 성토부(기준면이 경사진 원지반)이고,
    /// 실측 예로 <b>18㎡ vs 10㎡</b> — 80% 차이다. 어느 쪽인지가 계산 오차보다 훨씬 크다.
    /// 하니스 S78이 이 결정을 지키는지 지켜본다.</para></summary>
    /// <returns>(한계 이하 넓이, 한계 초과 넓이).</returns>
    public static (double Shallow, double Deep) SplitByDepth(double[] x, double[] top, double[] bot, double limit)
    {
        if (x == null || top == null || bot == null || !(limit > 0)) return (0, 0);
        int n = System.Math.Min(x.Length, System.Math.Min(top.Length, bot.Length));
        if (n < 2) return (0, 0);

        // ★★[검토] <b>깊이가 정확히 한계가 되는 자리에 점을 찍는다.</b>
        //   안 찍으면 그 칸을 사다리꼴로 세는데, 진짜 모양은 한계선에서 꺾이는 <b>삼각형</b>이다.
        //   검산: 폭 10m·깊이 0~10m·한계 5m → 참값 37.5/12.5인데 종전엔 25/25가 나왔다(비싼 쪽이 두 배).
        //   ※<c>Above</c>의 교차 처리가 이 자리를 안 잡아 주는 이유: 넘기는 값이 <c>max(0, 깊이−한계)</c>라
        //     <b>절대 음수가 안 되어</b> 부호가 바뀌는 일이 없다 — 교차 코드가 한 번도 안 돈다.
        {
            var add = new System.Collections.Generic.List<double>();
            for (int i = 0; i < n - 1; i++)
            {
                double a = top[i] - bot[i] - limit, b = top[i + 1] - bot[i + 1] - limit;
                if (double.IsNaN(a) || double.IsNaN(b)) continue;
                if ((a > 0 && b < 0) || (a < 0 && b > 0))
                    add.Add(x[i] + (x[i + 1] - x[i]) * (a / (a - b)));
            }
            if (add.Count > 0)
            {
                var nx = new System.Collections.Generic.List<double>(n + add.Count);
                for (int i = 0; i < n; i++) nx.Add(x[i]);
                nx.AddRange(add);
                nx.Sort();
                var xx = nx.ToArray();
                var tt = XsecQuantity.Resample(x, top, xx);
                var bb = XsecQuantity.Resample(x, bot, xx);
                return SplitCore(xx, tt, bb, limit);
            }
        }
        return SplitCore(x, top, bot, limit);
    }

    /// <summary>가르기 본체 — 경계점이 이미 x축에 들어 있다고 보고 잰다.</summary>
    private static (double Shallow, double Deep) SplitCore(double[] x, double[] top, double[] bot, double limit)
    {
        int n = System.Math.Min(x.Length, System.Math.Min(top.Length, bot.Length));
        if (n < 2) return (double.NaN, double.NaN);
        var cap = new double[n];
        for (int i = 0; i < n; i++)
        {
            double d = top[i] - bot[i];
            cap[i] = double.IsNaN(d) ? double.NaN : top[i] - System.Math.Min(d, limit);
        }
        double all = Above(x, top, bot);
        double deep = Above(x, cap, bot);      // 한계보다 깊은 몫
        if (double.IsNaN(all)) return (double.NaN, double.NaN);   // 못 쟀으면 둘 다 모른다
        if (double.IsNaN(deep)) deep = 0;
        double shallow = all - deep;
        if (shallow < 0) shallow = 0;
        return (shallow, deep);
    }

    /// <summary>두 선 중 <b>낮은 쪽</b>을 골라 새 선을 만든다 — 터파기 지표는
    /// <c>min(계획면, 원지반)</c>이다(성토 구간은 아직 흙이 없으니 원지반이 지표다).</summary>
    public static double[] Lower(double[] a, double[] b)
    {
        if (a == null) return b;
        if (b == null) return a;
        int n = System.Math.Min(a.Length, b.Length);
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double p = a[i], q = b[i];
            r[i] = double.IsNaN(p) ? q : double.IsNaN(q) ? p : System.Math.Min(p, q);
        }
        return r;
    }
}

/// <summary>횡단면 한 장에서 뽑은 수량(㎡). 값이 없으면 <c>NaN</c>이다 —
/// <b>0과 구별해야 한다</b>: 0은 "재 봤더니 없다"이고 NaN은 "잴 수 없었다"이다.
/// 표에서 전자는 <c>0.00</c>, 후자는 빈칸으로 간다.</summary>
public readonly record struct XsecQty(
    double Cut,            // 절토 — 원지반이 계획면보다 높은 만큼
    double Fill,           // 성토 — 계획면이 원지반보다 높은 만큼
    double ExcShallow,     // 터파기 5.0m 이하
    double ExcDeep,        // 터파기 5.0m 초과
    double Backfill)       // 되메우기
{
    /// <summary>어느 지표면을 못 읽었나 — 표가 빈칸일 때 <b>왜</b>인지 갈린다.</summary>
    public bool MissG { get; init; }
    public bool MissP { get; init; }
    public bool MissE { get; init; }

    /// <summary>터파기는 있는데 계획면이 없던 칸 수 — 0이 아니면 그만큼 <b>원지반 기준</b>으로 셌다는 뜻이다.
    /// 기준면이 조용히 바뀌면 터파기가 부풀어 오르므로 호출부가 로그로 알려야 한다.</summary>
    public int NoPlanCells { get; init; }

    /// <summary>터파기 합계 — 얕은 것과 깊은 것을 더한다.</summary>
    public double ExcTotal =>
        double.IsNaN(ExcShallow) && double.IsNaN(ExcDeep)
            ? double.NaN                                   // ★둘 다 못 쟀으면 "0"이 아니라 "모른다"
            : (double.IsNaN(ExcShallow) ? 0 : ExcShallow)
            + (double.IsNaN(ExcDeep) ? 0 : ExcDeep);
}

/// <summary>★★[JACK 0826] <b>한 측점의 수량을 낸다</b> — 절토·성토·터파기·되메우기.
///
/// <para>먼저 <b>세 선의 꺾임점을 한 자리에 모은다.</b> 원지반·계획면·터파기면은 각자 다른 자리에서
/// 꺾이는데, 그 합집합에서 재야 <b>꺾임을 하나도 안 놓친다</b>. TIN은 삼각형(평면) 조각이라
/// 꺾임점 사이는 진짜 직선이고, 그래서 사다리꼴 계산이 <b>근사가 아니라 정확값</b>이 된다.</para></summary>
public static class XsecQuantity
{
    /// <summary>터파기를 두 갈래로 가르는 깊이(m). 회사 수량표가 <c>5.0m 이하/이상</c>으로 나눈다.</summary>
    public const double DeepLimit = 5.0;

    /// <summary>한 측점의 수량. 없는 지표면은 <c>null</c>로 주면 그 항목이 <c>NaN</c>이 된다.</summary>
    /// <param name="gx">원지반 가로 위치(m)와 <paramref name="gy"/> 표고.</param>
    /// <param name="px">계획면. 없으면 null.</param>
    /// <param name="ex">터파기면. 없으면 null.</param>
    public static XsecQty Compute(
        double[] gx, double[] gy,
        double[] px, double[] py,
        double[] ex, double[] ey)
    {
        // ★★★[검토 0828 · M9] <b>조기 반환이 <c>MissG</c>를 못 켜고 나갔다.</b>
        //   호출부(<c>CollectQty</c>)가 <c>⚠원지반이 없던 측점 N개</c>를 찍으려고 이 깃발을 보는데,
        //   원지반을 못 읽는 <b>바로 그 길</b>에서 깃발이 <c>false</c>인 채로 나가 버렸다 —
        //   <b>그 경고는 영영 안 찍힌다</b>. Core를 직접 돌려 확인했다:
        //   <c>원지반 못읽음: MissG=False MissP=False MissE=False Cut=NaN</c>.
        //   JACK이 겪은 <i>"모든 측점이 −"</i>에서 <b>이유가 한 줄도 안 남은 까닭</b>이 이것이다.
        //   → <b>못 잰 이유를 켜서 내보낸다.</b> 값이 <c>NaN</c>인 것과 <b>왜 NaN인지</b>는 다른 정보다.
        if (gx == null || gy == null || gx.Length < 2)
            return new XsecQty(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN)
            { MissG = true, MissP = px == null || py == null, MissE = ex == null || ey == null };

        // ① 꺾임점을 모두 모아 하나의 가로축을 만든다.
        double[] x = Union(gx, px, ex);
        if (x.Length < 2)
            return new XsecQty(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN)
            { MissG = true, MissP = px == null || py == null, MissE = ex == null || ey == null };

        double[] G = Resample(gx, gy, x);
        double[] P = px != null && py != null ? Resample(px, py, x) : null;
        double[] E = ex != null && ey != null ? Resample(ex, ey, x) : null;

        // ② 절토·성토 — 원지반과 계획면 사이. 계획면이 없으면 잴 수 없다.
        double cut = P == null ? double.NaN : CrossSectionArea.Above(x, G, P);
        double fill = P == null ? double.NaN : CrossSectionArea.Above(x, P, G);

        // ③ 터파기 — 지표에서 굴착 바닥까지. <b>지표는 계획면과 원지반 중 낮은 쪽</b>이다:
        //    성토 구간은 아직 흙이 없으니 원지반이 지표이고, 절토 구간은 이미 깎았으니 계획면이 지표다.
        double shallow = double.NaN, deep = double.NaN, back = double.NaN;
        int noPlan = 0;   // 터파기는 있는데 계획면이 없는 칸 수 — 기준면이 원지반으로 바뀐 자리
        if (E != null)
        {
            // ★★[검토] <b>계획면이 없는 자리에서는 기준면이 조용히 원지반으로 바뀐다.</b>
            //   <c>Lower</c>는 한쪽이 NaN이면 다른 쪽을 쓰는데, 터파기 구간에 계획면이 안 깔려 있으면
            //   그 자리만 <b>원지반 기준</b>이 되어 터파기가 부풀어 오른다(실측 예: 50 → 62.5㎡).
            //   경고 없이 과다 계상되므로 <b>몇 칸이 그랬는지 세어</b> 호출부가 알 수 있게 한다.
            double[] top = P == null ? G : CrossSectionArea.Lower(G, P);
            if (P != null)
                for (int i = 0; i < E.Length && i < P.Length; i++)
                    if (!double.IsNaN(E[i]) && double.IsNaN(P[i]) && !double.IsNaN(G[i])) noPlan++;
            var sp = CrossSectionArea.SplitByDepth(x, top, E, DeepLimit);
            shallow = sp.Shallow;
            deep = sp.Deep;
            // ④ 되메우기 — ★★[JACK 0826] <b>판 것을 되채우는 것이므로 터파기를 넘을 수 없다.</b>
            //
            //    종전엔 <b>계획면</b>을 기준으로 쟀다. 절토부에서는 계획면이 곧 굴착 기준면이라 맞았지만,
            //    <b>성토부에서는 틀린다</b>: 원지반 95 · 계획 100 · 바닥 90이면
            //    터파기는 min(95,100)−90 = <b>5m</b>인데 되메우기가 100−90 = <b>10m</b>가 나왔다.
            //    그 위 5m(원지반→계획)는 되메우기가 아니라 <b>성토</b>이고 이미 성토로 세고 있으니
            //    <b>이중 계산</b>이다. 기준면을 터파기와 <b>같은 자</b>(낮은 쪽)로 맞춘다.
            //
            //    구조물이 차지하는 몫은 아직 못 뺀다(구조물 형상이 모델에 없다) — 그만큼 많게 나온다.
            back = shallow + deep;
        }
        return new XsecQty(cut, fill, shallow, deep, back)
        { NoPlanCells = noPlan, MissG = G == null, MissP = P == null, MissE = E == null };
    }

    /// <summary>세 가로축의 <b>합집합</b> — 겹치는 값은 하나로 친다(1mm 안이면 같은 자리).</summary>
    public static double[] Union(params double[][] arrays)
    {
        var all = new System.Collections.Generic.List<double>();
        foreach (var a in arrays)
            if (a != null)
                foreach (double v in a)
                    if (!double.IsNaN(v)) all.Add(v);
        if (all.Count == 0) return System.Array.Empty<double>();
        all.Sort();
        var r = new System.Collections.Generic.List<double>(all.Count) { all[0] };
        for (int i = 1; i < all.Count; i++)
            if (all[i] - r[r.Count - 1] > 1e-3) r.Add(all[i]);
        return r.ToArray();
    }

    /// <summary>꺾은선을 새 가로축 위에서 <b>선형 보간</b>한다. 범위 밖은 <c>NaN</c> —
    /// 없는 자리를 끝값으로 늘리면 <b>있지도 않은 흙을 세게 된다</b>.</summary>
    public static double[] Resample(double[] sx, double[] sy, double[] dx)
    {
        var r = new double[dx.Length];
        int n = System.Math.Min(sx.Length, sy.Length);
        for (int k = 0; k < dx.Length; k++)
        {
            double t = dx[k];
            if (n < 2 || t < sx[0] - 1e-9 || t > sx[n - 1] + 1e-9) { r[k] = double.NaN; continue; }
            int i = 0;
            while (i < n - 2 && sx[i + 1] < t) i++;
            double x0 = sx[i], x1 = sx[i + 1], w = x1 - x0;
            // ★★[검토] 노드와 <b>같은 자리</b>면 보간하지 않고 그 값을 그대로 쓴다.
            //   종전엔 앞 점이 NaN이면 <c>NaN + (값−NaN)×1 = NaN</c>이 되어
            //   <b>유효한 점 하나를 잡아먹었다</b> — 지표면 왼쪽 가장자리에서만 0.25m가 더 빠졌다(좌우 비대칭).
            if (System.Math.Abs(t - x0) < 1e-9) { r[k] = sy[i]; continue; }
            if (System.Math.Abs(t - x1) < 1e-9) { r[k] = sy[i + 1]; continue; }
            r[k] = w <= 1e-12 ? sy[i] : sy[i] + (sy[i + 1] - sy[i]) * (t - x0) / w;
        }
        return r;
    }
}
