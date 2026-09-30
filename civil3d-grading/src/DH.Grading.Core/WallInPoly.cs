using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0918] <b>손으로 그린 폴리곤 안에 정형화된 가상 옹벽을 세운다.</b>
///
/// <para>JACK: <i>"이제 안쪽 해당 폴리곤 안에 속하는 <b>원지반 높이보다 높은 단</b>
/// (단높이는 매개변수에 따름)까지 가상 옹벽을 치는 걸 추가해."</i></para>
///
/// <para>그리고 그 앞에 정한 것:
/// <i>"<b>선택구간</b>, 비스듬히 선택하는 구간 <b>시종점 2곳 선분</b>만 옹벽,
/// 나머지 폴리곤을 닫기 위한 선분은 <b>설정에 관계없이 수직</b>으로 침."</i></para>
///
/// <code>
///                 폐합면(데이라잇) — <b>수직</b>, 옹벽 아님
///        ┌─────────────────────────┐
///        │ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒ │  ← 줄이 단마다 안쪽으로 물러난다
///   측선 │ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒ │ 측선   (셋 다 옹벽)
///        └─────────────────────────┘
///                 선택구간(옹벽)
/// </code>
///
/// <para><b>왜 이 길인가.</b> 0916~0917에 줄마다 3D로 잘라 붙이는 길을 갔다가 접었다 —
/// 가짜 데이라잇 30% · 덩이 링이 발자국의 6배 · 한 면에 Outer 여럿이면 삼각형 1311→137.
/// JACK: <i>"절대로 3D면이나 그런 걸 하나하나 생성하지 않을 거야.
/// <b>정형화된 옹벽</b> 만들고 <b>무조건 데이라잇으로 자르는</b> 식 갈 거야."</i></para></summary>
public static class WallInPoly
{
    /// <summary>★[하네스 S136] <b>마이터 상한</b> — 아주 뾰족한 코너에서 무한히 길어지지 않게.
    /// <para>4배면 약 29도까지 제대로 맞물리고, 그보다 뾰족하면 잘린다(잘려도 접히지는 않는다).</para></summary>
    private const double MiterCap = 4.0;

    /// <summary>한 단이 <b>평면에서 가는 거리</b>(면 + 소단).
    /// <para>면은 <c>단높이 × 구배</c>인데, 구배가 0이면 폭이 0이 되어 줄이 겹친다 —
    /// <see cref="GradingParams.MinFaceRun"/>이 그 바닥을 지킨다.</para></summary>
    public static double StepRun(double benchH, double slope, double benchW, double minFaceRun)
        => Math.Max(benchH * Math.Max(0, slope), Math.Max(1e-4, minFaceRun)) + Math.Max(0, benchW);

    /// <summary>★[v102.0 · 계획 검토 물음] 직전 <see cref="RowsByBuffer"/>가 <b>남는 자리가 없어 버린 줄</b> 수 —
    /// 0보다 크면 옹벽이 원지반 너머까지 못 가 윗면(절토)·바닥(성토)이 드러난다 → 부르는 쪽이 합성을 막는다.</summary>
    [ThreadStatic] public static int LastLost;

    /// <summary>★★[검토 0918 · 높음 4] <b>줄을 닫아서 브레이크라인으로 넣는다.</b>
    /// <para><see cref="GradingGeometry.PolyMinusLineBuffer"/>는 닫는 점을 떼고 돌려주는데,
    /// 줄은 <b>열린 브레이크라인</b>으로 들어간다(<c>BuildVirtualSlope</c>의 cornerLines).
    /// 그러면 줄마다 <b>마지막 점 → 첫 점 변</b>이 빠져 그 자리는 Civil이 마음대로 삼각형을 잇는다 —
    /// 곧은 변 하나가 통째로 빠질 수 있다. 첫 점을 끝에 한 번 더 붙여 닫는다.</para></summary>
    public static List<List<Point3>> ClosedRows(IReadOnlyList<List<Point3>> rows)
    {
        var res = new List<List<Point3>>(rows.Count);
        foreach (var r in rows)
        {
            var c = new List<Point3>(r);
            if (c.Count >= 3)
            {
                var f = c[0]; var l = c[c.Count - 1];
                if (Math.Abs(f.X - l.X) > 1e-9 || Math.Abs(f.Y - l.Y) > 1e-9) c.Add(f);
            }
            res.Add(c);
        }
        return res;
    }

    /// <summary>★[JACK 0918] 열린 선의 <b>양 끝을 곧게 늘인다</b>.
    /// <para>버퍼를 뜰 때 끝마개 때문에 <b>끝에서 offset이 0으로 줄어드는</b> 것을 막는다 —
    /// 늘인 부분은 폴리곤 밖으로 나가므로 결과에 영향이 없다.</para></summary>
    public static List<Point3> ExtendEnds(IReadOnlyList<Point3> line, double by)
    {
        var r = new List<Point3>();
        if (line == null || line.Count < 2) { if (line != null) r.AddRange(line); return r; }
        int n = line.Count;
        double d0x = line[0].X - line[1].X, d0y = line[0].Y - line[1].Y;
        double L0 = Math.Sqrt(d0x * d0x + d0y * d0y);
        if (L0 > 1e-9) r.Add(new Point3(line[0].X + d0x / L0 * by, line[0].Y + d0y / L0 * by, line[0].Z));
        r.AddRange(line);
        double d1x = line[n - 1].X - line[n - 2].X, d1y = line[n - 1].Y - line[n - 2].Y;
        double L1 = Math.Sqrt(d1x * d1x + d1y * d1y);
        if (L1 > 1e-9) r.Add(new Point3(line[n - 1].X + d1x / L1 * by, line[n - 1].Y + d1y / L1 * by, line[n - 1].Z));
        return r;
    }

    /// <summary>★<b>폴리곤 안의 원지반 최고 표고</b>. 못 재면 <c>null</c>.
    /// <para>테두리와 안쪽을 <paramref name="grid"/> 간격 격자로 훑는다 —
    /// 테두리만 보면 가운데 봉우리를 놓친다.</para></summary>
    public static double? MaxGroundIn(IReadOnlyList<Point3> ring,
        Func<double, double, double?> ground, double grid, out int nHit, out int nMiss)
    {
        nHit = 0; nMiss = 0;
        if (ring == null || ring.Count < 3) return null;
        double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
        foreach (var q in ring)
        {
            mnx = Math.Min(mnx, q.X); mny = Math.Min(mny, q.Y);
            mxx = Math.Max(mxx, q.X); mxy = Math.Max(mxy, q.Y);
        }
        double g = Math.Max(0.5, grid);
        double best = double.MinValue;
        // ①안쪽 격자
        for (double x = mnx; x <= mxx + 1e-9; x += g)
            for (double y = mny; y <= mxy + 1e-9; y += g)
            {
                if (!GradingGeometry.PointInRing(ring, x, y)) continue;
                var v = ground(x, y);
                if (v == null) { nMiss++; continue; }
                nHit++; if (v.Value > best) best = v.Value;
            }
        // ②테두리 — 좁고 긴 폴리곤이면 격자가 한 점도 안 걸릴 수 있다
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = Math.Max(1, (int)Math.Ceiling(L / g));
            for (int k = 0; k <= n; k++)
            {
                double t = (double)k / n;
                var v = ground(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                if (v == null) { nMiss++; continue; }
                nHit++; if (v.Value > best) best = v.Value;
            }
        }
        return best == double.MinValue ? (double?)null : best;
    }

    /// <summary>★<b>몇 단을 쌓아야 원지반을 넘는가</b>.
    /// <para>JACK: <i>"원지반 높이<b>보다 높은</b> 단까지"</i> — 같은 높이로는 모자라니 <b>넘을 때까지</b>다.</para></summary>
    public static int BenchCount(double z0, double topGround, double benchH, int cap = 200)
    {
        if (benchH <= 1e-9) return 1;
        double d = topGround - z0;
        if (d <= 0) return 1;                       // 이미 넘었다 — 그래도 한 단은 세운다
        int n = (int)Math.Ceiling(d / benchH);
        if (n * benchH <= d + 1e-9) n++;            // <b>같으면 한 단 더</b> — "보다 높은"이므로
        return Math.Max(1, Math.Min(cap, n));
    }

    /// <summary>★★[v102.0 · JACK 0929 «성토 — 절토의 거울, 계단으로 내려감»] <b>폴리곤 안의 원지반 최저 표고</b> — <see cref="MaxGroundIn"/>의 거울.
    /// <para>−z로 뒤집어 같은 함수로 잰다(같은 격자·같은 테두리 표본 — 절토와 같은 정확도). 못 재면 <c>null</c>.</para></summary>
    public static double? MinGroundIn(IReadOnlyList<Point3> ring,
        Func<double, double, double?> ground, double grid, out int nHit, out int nMiss)
    {
        var v = MaxGroundIn(ring, (x, y) => { var g = ground(x, y); return g == null ? (double?)null : -g.Value; },
                            grid, out nHit, out nMiss);
        return v == null ? (double?)null : -v.Value;
    }

    /// <summary>★★[v102.0] <b>몇 단을 내려가야 원지반 아래로 가는가</b> — <see cref="BenchCount"/>의 거울
    /// (z0 − n·단높이 &lt; 원지반 최저가 되는 가장 작은 n · 딱 떨어지면 한 단 더 · 이미 아래면 1).</summary>
    public static int BenchCountDown(double z0, double bottomGround, double benchH, int cap = 200)
        => BenchCount(-z0, -bottomGround, benchH, cap);

    /// <summary>★★[v102.0 · JACK 0929 «성토=구간 최고점, 절토=구간 최저점»] <b>옹벽 머리(첫 줄) 높이</b> — 계획선 구간 [t0, t0+span]의
    /// 최저(절토)·최고(성토).
    /// <para>폴리곤 테두리에서 옹벽 첫 줄(W = z0)이 절토면 계획면 <b>이하</b>, 성토면 <b>이상</b>이어야 합성이 바깥 정지면과 턱 없이 잇는다
    /// (절토 max(P, min(W,G)) · 성토 min(P, max(W,G)) 둘 다 테두리에서 P). 구간 시작점만 쓰면 기운 계획선에서 턱이 나 판정 3이었다.</para>
    /// <para>선분 안은 1차식이라 극값은 <b>두 끝과 사이 꺾인점</b>에만 있다 — 그 셋만 본다(정확). 평평하면 시작점과 같다(현장 105m).
    /// 구간이 0을 지나도(t0 + span &gt; 둘레) · 한 바퀴여도 된다(<see cref="GradingGeometry.PointAtParam"/>과 같은 고리 규약).</para></summary>
    /// <param name="line">구간을 잰 자(계획 경계 또는 그 단의 링 — 닫는 점 반복은 있어도 없어도).</param>
    /// <param name="cum"><see cref="GradingGeometry.CumLen2D"/>(line).</param>
    /// <param name="up">절토인가(참 = 최저 · 거짓 = 최고).</param>
    public static double HeadZ(IReadOnlyList<Point3> line, double[] cum, double t0, double span, bool up)
    {
        if (line == null || line.Count == 0 || cum == null || cum.Length == 0)
            throw new ArgumentException("HeadZ — 구간을 잰 자(선)가 비었다");      // ★[재검토 4] 조용한 NaN은 폴리곤·옹벽 줄 높이로 퍼진다
        double z = GradingGeometry.PointAtParam(line, cum, t0).Z;
        double tot = cum[cum.Length - 1];
        if (line.Count < 2 || tot < 1e-12) return z;
        double zE = GradingGeometry.PointAtParam(line, cum, t0 + Math.Max(0, span)).Z;
        double best = up ? Math.Min(z, zE) : Math.Max(z, zE);
        bool whole = span >= tot - 1e-9;
        int m = Math.Min(cum.Length - 1, line.Count);       // 닫는 점 반복을 뺀 꼭짓점 수
        for (int i = 0; i < m; i++)
        {
            double rel = ((cum[i] - t0) % tot + tot) % tot;  // t0에서 고리를 따라 간 거리
            if (whole || (rel > 0 && rel < span))
                best = up ? Math.Min(best, line[i].Z) : Math.Max(best, line[i].Z);
        }
        return best;
    }

    /// <summary>★★★[JACK 0918 스샷 <i>"옹벽과 수직벽이 <b>닿는 부분이 깨져</b>"</i>]
    /// <b>줄을 「옹벽선에서 d만큼 떨어진 자리」로 짓는다 — NTS에게 직접 묻는다.</b>
    ///
    /// <para><b>점마다 미는 방식이 한계였다.</b> 옹벽 모서리 점이 폐합면을 따라 미끄러지며
    /// <b>지나쳐 가고</b>, NTS가 겹침을 정리해도 <b>가는 돌기</b>가 남는다 —
    /// 그 돌기들이 줄마다 쌓여 <b>칼날 같은 삼각형</b>이 선다(JACK 스샷의 그것).</para>
    ///
    /// <para>★그래서 <b>정의대로</b> 짓는다:
    /// <c>줄 = 폴리곤 − (옹벽선에서 d 안쪽)</c>. NTS가 코너도 트림도 <b>스스로</b> 한다.</para>
    ///
    /// <code>
    ///   폴리곤 ─────────────────
    ///   옹벽선에서 d ▓▓▓▓▓▓▓▓      빼면 남는 테두리가 <b>그 줄</b>이다
    ///   ────────────────────────
    /// </code>
    ///
    /// <para>폐합면은 옹벽선에서 멀어 <b>안 깎인다</b> — 다만 <paramref name="vEps"/>만큼
    /// 폴리곤 전체를 아주 조금 줄여, 위아래 줄이 평면에서 <b>겹치지 않게</b> 한다
    /// (겹치면 넓이 0짜리 삼각형이 되어 톱니가 난다).</para></summary>
    /// <param name="wallLine">옹벽이 서는 <b>열린 선</b>(측선 → 선택구간 → 측선).</param>
    /// <param name="down">★★[v102.0] 성토 — 단마다 <b>내려간다</b>(zTop = z0 − 단높이·k). 물러나는 방향·폭은 절토와 같다
    /// (옹벽선에서 폴리곤 안쪽 = 사면 쪽으로). 줄의 XY는 절토와 비트로 같고 z만 다르다.</param>
    public static List<List<Point3>> RowsByBuffer(IReadOnlyList<Point3> poly, IReadOnlyList<Point3> wallLine,
        double z0, int benches, double benchH, double slope, double benchW, double minFaceRun,
        out string log, bool down = false)
    {
        double sgnZ = down ? -1.0 : 1.0;                // ×1.0·×(−1.0)은 정확 — 절토 줄은 종전과 비트로 같다
        string lastRow = down ? "맨 아래 줄" : "맨 위 줄";
        LastLost = 0;
        var rows = new List<List<Point3>>();
        var sb = new System.Text.StringBuilder();
        if (poly == null || poly.Count < 3 || wallLine == null || wallLine.Count < 2)
        { log = "줄을 만들 재료가 없다"; return rows; }

        double face = Math.Max(benchH * Math.Max(0, slope), Math.Max(1e-4, minFaceRun));
        double step = face + Math.Max(0, benchW);
        double vEps = Math.Max(1e-3, minFaceRun);
        int lost = 0, rowIx = 0;

        // ══ ★★★[JACK 0918 스샷 <i>"전혀 해결된 게 없어"</i>] <b>옹벽선을 양쪽으로 늘여 놓고 버퍼를 뜬다.</b>
        //
        //   <para><b>단이 폐합면에 닿기 전에 사라지고 매끈한 램프가 되던 까닭.</b>
        //   옹벽선의 <b>끝</b>이 폐합면에 딱 붙어 있는데, 버퍼를 <b>평평한 끝마개</b>로 뜨니
        //   <b>끝을 지나면 버퍼가 없다</b>. 그래서 물러난 거리가 끝으로 갈수록
        //   <b>d에서 0으로 줄어들며</b> 삼각형 램프가 된다 — 스샷의 그것이다.</para>
        //
        //   <code>
        //     ✕ 끝에서 끊긴 버퍼        ○ 늘여 놓고 뜬 버퍼
        //        ▓▓▓▓▓▓╲                  ▓▓▓▓▓▓▓▓▓▓▓
        //        ──────┴ 폐합면           ──────┴ 폐합면 (끝까지 d)
        //   </code>
        //
        //   <para>★내 잣대가 이걸 못 봤다 — 「양 끝 8%는 안 본다」로 재고 있었는데
        //   <b>하필 문제가 그 8%에 있었다</b>. 이제 늘이므로 그 자리도 제값이 나온다.</para>
        double maxOff = step * Math.Max(0, benches - 1) + face;
        var wl = ExtendEnds(wallLine, maxOff + 5.0);

        List<Point3>? Cut(double d, double z)
        {
            var r = GradingGeometry.PolyMinusLineBuffer(poly, wl, d, vEps * rowIx, z);
            rowIx++;
            return r;
        }

        var r0 = Cut(0, z0);
        if (r0 != null) rows.Add(r0); else { rows.Add(new List<Point3>(poly)); rowIx = 1; }
        for (int k = 1; k <= benches; k++)
        {
            double zTop = z0 + sgnZ * benchH * k;
            var rf = Cut(step * (k - 1) + face, zTop);            // 면 끝
            if (rf != null) rows.Add(rf); else lost++;
            if (k < benches)
            {
                var rb = Cut(step * k, zTop);                     // 소단 끝
                if (rb != null) rows.Add(rb); else lost++;
            }
        }
        LastLost = lost;

        sb.Append($"단 <b>{benches}</b>개{(down ? "(<b>성토 — 아래로</b>)" : "")} · 줄 <b>{rows.Count}</b>개(NTS로 깎음 · 한 단에 둘 · 마지막 단은 면에서 끝)");
        sb.Append($" · 면 {face:0.###}m + 소단 {Math.Max(0, benchW):0.##}m = 한 단 {step:0.###}m");
        sb.Append($" · 표고 {z0:F2} → <b>{z0 + sgnZ * benchH * benches:F2}m</b>");
        if (lost > 0) sb.Append($" · <b>⚠남는 자리가 없어 버린 줄 {lost}개</b>(단을 너무 많이 {(down ? "내렸다" : "쌓았다")} — 옹벽이 원지반 너머까지 못 가 {(down ? "바닥" : "윗면")}이 드러난다)");
        int nBad = 0; foreach (var r in rows) if (!GradingGeometry.RingIsSimple(r)) nBad++;
        if (nBad > 0) sb.Append($" · <b>⚠제 몸을 지르는 줄 {nBad}개</b>");

        // ══ ★★★[JACK 0918 <i>"니가 보기엔 저게 좋아진 거냐?"</i>] <b>변마다 얼마나 물러났는지 잰다.</b>
        //
        //   <para>스샷에서 <b>측선에는 단이 없고</b> 비탈 하나였는데 <b>로그엔 경고가 없었다</b> —
        //   그러면 로그가 <b>볼 줄 모르는 것</b>을 보고 있는 것이다. 그래서 잣대를 더 만든다.</para>
        //
        //   <para>옹벽선을 <b>세 토막</b>(측선·선택구간·측선)으로 나눠, 맨 위 줄이 각 토막에서
        //   <b>얼마나 떨어졌는지</b> 잰다. 제대로 물러났으면 셋 다 «단수 × 한 단»에 가깝고,
        //   <b>안 물러난 토막은 0</b>으로 나온다 — 그 한 줄이면 이번 같은 일을 바로 안다.</para>
        if (rows.Count >= 2)
        {
            var top = rows[rows.Count - 1];
            double want = step * (benches - 1) + face;
            static double NearLine(IReadOnlyList<Point3> line, int i0, int i1, IReadOnlyList<Point3> row)
            {
                double best = double.MaxValue;
                for (int i = i0; i < i1; i++)
                {
                    double d = double.MaxValue;
                    for (int k = 0; k < row.Count; k++)
                    {
                        var a1 = row[k]; var b1 = row[(k + 1) % row.Count];
                        double ex = b1.X - a1.X, ey = b1.Y - a1.Y, L2 = ex * ex + ey * ey;
                        double t = L2 < 1e-18 ? 0
                            : Math.Max(0, Math.Min(1, ((line[i].X - a1.X) * ex + (line[i].Y - a1.Y) * ey) / L2));
                        double dx = line[i].X - (a1.X + ex * t), dy = line[i].Y - (a1.Y + ey * t);
                        d = Math.Min(d, dx * dx + dy * dy);
                    }
                    best = Math.Min(best, d);
                }
                return best == double.MaxValue ? -1 : Math.Sqrt(best);
            }
            // ★<b>양 끝은 뺀다.</b> 옹벽선의 끝은 폐합면에 닿아 있어 줄이 바로 옆을 지난다 —
            //   거기를 넣고 최솟값을 재면 <b>언제나 0에 가깝게</b> 나와 거짓 경보가 된다
            //   (하네스 S137이 같은 코드로 셋 다 3.20m를 재는데 이 줄만 0.04m를 찍었다).
            // ★<b>끝까지 본다.</b> 종전엔 양 끝 8%를 빼고 쟀는데 <b>하필 문제가 그 8%에 있었다</b> —
            //   그래서 램프가 생겼는데도 "앞 3.20 · 뒤 3.20"으로 나왔다.
            //   이제 옹벽선을 늘여 버퍼를 뜨므로 끝까지 제값이어야 한다.
            int m = wallLine.Count;
            int t1 = m / 3, t2 = 2 * m / 3;
            double dA = NearLine(wallLine, 0, Math.Max(1, t1), top);
            double dB = NearLine(wallLine, t1, Math.Max(t1 + 1, t2), top);
            double dC = NearLine(wallLine, t2, m, top);
            sb.Append($" · <b>물러난 거리</b>({lastRow} · 기대 {want:F2}m) — 앞 {dA:F2} · 가운데 {dB:F2} · 뒤 {dC:F2}m");
            double worst = Math.Min(dA, Math.Min(dB, dC));
            if (worst < want * 0.5)
                sb.Append($" · <b>⚠한 토막이 거의 안 물러났다</b>({worst:F2}m) — 그 자리엔 <b>단이 안 선다</b>");
        }
        log = sb.ToString();
        return rows;
    }

    /// <summary>★★★[JACK 0918 스샷 <i>"<b>폴리곤대로 만들어지지 않았어</b>, 옹벽면만 만들어졌지.
    /// 그게 아니라 시점·종점 연장선으로 닫은 부분은 <b>옹벽이 아닌 수직</b>으로 <b>같은 높이만큼 올라가서</b>
    /// 만들어져야지"</i>] <b>폴리곤 전체로 줄을 만든다 — 변마다 규칙이 다르다.</b>
    ///
    /// <code>
    ///        폐합면 — <b>수직</b>(제자리에서 같은 높이만큼 올라간다)
    ///     ┌───────────────────────┐
    ///     │▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏│  ← 줄이 단마다 안쪽으로 물러난다
    ///  측선│▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏│측선   (셋 다 <b>옹벽</b>)
    ///     └───────────────────────┘
    ///             선택구간
    /// </code>
    ///
    /// <para><b>첫 판이 틀렸다.</b> 옹벽이 서는 <b>열린 선</b>만 줄로 만들었더니 <b>옹벽면만</b> 생기고
    /// 폴리곤의 나머지(폐합면 쪽)가 <b>비었다</b>. 줄을 <b>닫힌 링</b>으로 만들되,
    /// 옹벽 변은 물러나고 폐합면 변은 <b>제자리</b>에 두면 한 번에 해결된다 —
    /// 맨 위 줄이 곧 <b>뚜껑의 테두리</b>가 되어 폴리곤이 다 채워진다.</para></summary>
    /// <param name="ring">폴리곤(닫힌 링).</param>
    /// <param name="isWall">점마다 <b>옹벽 변인가</b>. 거짓이면 그 점은 <b>안 물러난다</b>(수직).</param>
    public static List<List<Point3>> RowsInRing(IReadOnlyList<Point3> ring, IReadOnlyList<bool> isWall,
        double z0, int benches, double benchH, double slope, double benchW, double minFaceRun,
        out string log)
    {
        var rows = new List<List<Point3>>();
        var sb = new System.Text.StringBuilder();
        if (ring == null || ring.Count < 3 || isWall == null || isWall.Count != ring.Count)
        { log = "줄을 만들 재료가 없다(링과 표가 안 맞는다)"; return rows; }

        int n = ring.Count;
        double face = Math.Max(benchH * Math.Max(0, slope), Math.Max(1e-4, minFaceRun));
        double step = face + Math.Max(0, benchW);

        // ★<b>안쪽</b>이 어느 쪽인가 — 링의 감김으로 정한다(반시계면 진행 방향의 왼쪽이 안).
        double a2 = 0;
        for (int i = 0; i < n; i++)
        { var u = ring[i]; var v = ring[(i + 1) % n]; a2 += u.X * v.Y - v.X * u.Y; }
        double sgn = a2 > 0 ? 1.0 : -1.0;

        // ══ ★★★[하네스 S136] <b>코너에서 접힌다 — 마이터로 맞물린다.</b> ═══════════════
        //
        //   <para><b>실측</b>: 8줄 중 <b>4줄</b>이 제 몸을 질렀다. 지르기 시작한 자리는
        //   물러난 거리가 <b>점 간격(2m)</b>을 넘어선 4번 줄(2.10m)부터였다.</para>
        //
        //   <para><b>까닭.</b> 점마다 법선으로 <c>d</c>만큼 밀면, <b>코너</b>에서는 두 변이 각각
        //   <c>d</c>만큼 안으로 들어가는데 <b>꼭짓점은 <c>d</c>밖에 안 들어간다</b> —
        //   그만큼 모자라 이웃 마디가 서로 넘어간다. 코너가 뾰족할수록 심하다.</para>
        //
        //   <code>
        //     ✕ 점마다 d       ○ 마이터(두 변의 offset 선이 <b>만나는 자리</b>)
        //        ╲ ╱ ← 모자람        ╲   ╱
        //         ●                   ╲ ╱
        //                              ●  ← d / cos(θ/2)
        //   </code>
        //
        //   <para>★<b>변</b>의 법선으로 셈한다(꼭짓점 평균이 아니라). 그리고 옹벽 점은
        //   <b>옹벽 변</b>만 본다 — 폐합면 변은 안 움직이므로 끌려가면 안 된다.</para>
        //   <para>아주 뾰족한 코너는 마이터가 무한히 길어지므로 <b>MiterCap배</b>로 자른다.</para>
        var nx = new double[n]; var ny = new double[n];
        {
            // 변 j = ring[j] → ring[j+1] · 그 변이 <b>옹벽 변</b>인가(양 끝이 다 옹벽이어야)
            var ex = new double[n]; var ey = new double[n]; var ew = new bool[n];
            for (int j = 0; j < n; j++)
            {
                int k2 = (j + 1) % n;
                double tx = ring[k2].X - ring[j].X, ty = ring[k2].Y - ring[j].Y;
                double L = Math.Sqrt(tx * tx + ty * ty);
                if (L < 1e-12) { ex[j] = 0; ey[j] = 0; ew[j] = false; continue; }
                ex[j] = sgn * (-ty / L); ey[j] = sgn * (tx / L);     // 그 변의 <b>안쪽</b> 법선
                ew[j] = isWall[j] && isWall[k2];
            }
            for (int i = 0; i < n; i++)
            {
                int jp0 = (i - 1 + n) % n;
                if (!isWall[i])
                {
                    // ★폐합면도 <b>법선이 있어야</b> 아주 조금씩 밀 수 있다 — 두 이웃 변의 평균으로.
                    double vx = ex[jp0] + ex[i], vy = ey[jp0] + ey[i];
                    double vL = Math.Sqrt(vx * vx + vy * vy);
                    if (vL < 1e-12) { nx[i] = ex[i]; ny[i] = ey[i]; }
                    else { nx[i] = vx / vL; ny[i] = vy / vL; }
                    continue;
                }
                int jp = jp0, jn = i;                                // 들어오는 변 · 나가는 변
                bool up = ew[jp], un = ew[jn];
                double ax, ay;
                if (up && un)
                {
                    // 마이터 — 두 변의 offset 선이 만나는 자리
                    double mx = ex[jp] + ex[jn], my = ey[jp] + ey[jn];
                    double mL = Math.Sqrt(mx * mx + my * my);
                    if (mL < 1e-9) { ax = ex[jn]; ay = ey[jn]; }     // 180도로 꺾였다 — 한쪽만
                    else
                    {
                        mx /= mL; my /= mL;
                        double cos = mx * ex[jn] + my * ey[jn];      // = cos(θ/2)
                        double k3 = cos < 1e-6 ? MiterCap : Math.Min(MiterCap, 1.0 / cos);
                        ax = mx * k3; ay = my * k3;
                    }
                }
                else if (up) { ax = ex[jp]; ay = ey[jp]; }
                else if (un) { ax = ex[jn]; ay = ey[jn]; }
                else { ax = 0; ay = 0; }
                nx[i] = ax; ny[i] = ay;
            }
        }

        // ══ ★★★[JACK 0918 스샷 <i>"수직 부분이 옹벽 높이와 같이 똑같이 올라오고 옹벽 부분과
        //   <b>깔끔하게 떨어져야</b> 하는데 <b>톱니처럼 깨지고</b> 이상하게 만들어져"</i>] ═══════════
        //
        //   <para><b>진짜 수직은 TIN이 못 그린다.</b> 평면에서 폭이 <b>0</b>이면 위아래 줄이 같은 자리라
        //   삼각형이 넓이 0이 된다 — 그 자리가 <b>톱니</b>로 깨진다.</para>
        //
        //   <para>이 저장소는 옹벽에서 <b>이미 같은 벽에 부딪혔고</b> 그래서 수직을 <b>1:0.01</b>로 친다
        //   (<c>MinSlope</c> · <c>MinFaceRun</c>): 5m 높이에 평면 <b>5cm</b>. 폐합면도 같은 처방을 쓴다.</para>
        //
        //   <para>★다만 <b>줄마다</b> 아주 조금씩 민다(단마다가 아니라). 한 단에 줄이 둘인데
        //   그 둘이 같은 표고·같은 자리면 <b>점이 겹쳐</b> 또 넓이 0이 되기 때문이다.
        //   8줄이면 다 합쳐 0.04m — 눈으로는 수직이다.</para>
        double vEps = Math.Max(1e-3, minFaceRun);        // 줄 하나가 미는 아주 작은 거리
        int nWall = 0; foreach (var b in isWall) if (b) nWall++;
        int rowIx = 0;
        List<Point3> Row(double off, double z)
        {
            double vOff = vEps * rowIx++;                // 폐합면 — 줄마다 아주 조금(눈으로는 수직)
            var r = new List<Point3>(n);
            for (int i = 0; i < n; i++)
            {
                double d = isWall[i] ? off : vOff;
                r.Add(new Point3(ring[i].X + nx[i] * d, ring[i].Y + ny[i] * d, z));
            }
            return r;
        }

        // ★★★[하네스 S136] 민 줄은 <b>NTS로 정리하고 쓴다</b>.
        //   <para>코너에서 두 변의 offset 선이 <b>서로를 지나가기</b> 때문이다 —
        //   물러난 거리가 점 간격보다 크면 반드시 난다(실측: 간격 2m · 물러남 2.10m부터).
        //   마이터로 꼭짓점을 옮겨도 이웃 마디의 overshoot은 그대로라 안 풀린다.</para>
        int nFixed = 0, nLost = 0;
        void Push(double off, double z)
        {
            var raw = Row(off, z);
            if (off <= 1e-9) { rows.Add(raw); return; }           // 첫 줄은 폴리곤 그대로다
            if (GradingGeometry.RingIsSimple(raw)) { rows.Add(raw); return; }
            var fix = GradingGeometry.CleanRingNts(raw, z);
            if (fix != null && fix.Count >= 3) { rows.Add(fix); nFixed++; }
            else nLost++;                                         // 못 살렸다 — 그 줄은 버린다
        }
        Push(0, z0);
        for (int k = 1; k <= benches; k++)
        {
            double zTop = z0 + benchH * k;
            Push(step * (k - 1) + face, zTop);                    // 면 끝
            if (k < benches) Push(step * k, zTop);                // 소단 끝(마지막 단은 면에서 끝)
        }

        sb.Append($"단 <b>{benches}</b>개 · 줄 <b>{rows.Count}</b>개(닫힌 링 · 한 단에 둘 · 마지막 단은 면에서 끝)");
        sb.Append($" · 점 {n}개 중 <b>옹벽 {nWall}</b> · <b>수직 {n - nWall}</b>(폐합면)");
        sb.Append($" · 면 {face:0.###}m + 소단 {Math.Max(0, benchW):0.##}m = 한 단 {step:0.###}m");
        sb.Append($" · 표고 {z0:F2} → <b>{z0 + benchH * benches:F2}m</b>");
        if (nFixed > 0) sb.Append($" · 코너 겹침을 <b>정리한 줄 {nFixed}개</b>");
        if (nLost > 0) sb.Append($" · <b>⚠못 살려 버린 줄 {nLost}개</b>(단을 너무 많이 쌓아 안쪽에서 만났다)");
        int nBad = 0; foreach (var r in rows) if (!GradingGeometry.RingIsSimple(r)) nBad++;
        if (nBad > 0) sb.Append($" · <b>⚠아직 제 몸을 지르는 줄 {nBad}개</b>");
        log = sb.ToString();
        return rows;
    }

    /// <summary>★★★<b>줄을 만든다.</b> 한 단에 둘 — <b>면 끝</b>과 <b>소단 끝</b>.
    ///
    /// <para><paramref name="chain"/>은 옹벽이 서는 <b>열린 선</b>이다
    /// (측선 → 선택구간 → 측선). 폐합면은 <b>안 들어간다</b> — 거기는 수직이다.</para>
    ///
    /// <para>줄은 단마다 <b>폴리곤 안쪽으로</b> 물러난다. 물러난 줄이 폴리곤 밖으로 나가면
    /// 그 점은 <b>버린다</b> — 벽은 폴리곤 안에만 선다.</para></summary>
    /// <param name="inward">안쪽 방향 — 줄을 미는 쪽. 점마다 법선의 부호를 정하는 데만 쓴다.</param>
    /// <returns>줄들(아래부터). 각 줄은 표고가 일정하다.</returns>
    public static List<List<Point3>> Rows(IReadOnlyList<Point3> chain, IReadOnlyList<Point3> poly,
        double z0, int benches, double benchH, double slope, double benchW, double minFaceRun,
        (double X, double Y) inward, out string log)
    {
        var rows = new List<List<Point3>>();
        var sb = new System.Text.StringBuilder();
        if (chain == null || chain.Count < 2 || poly == null || poly.Count < 3)
        { log = "줄을 만들 재료가 없다"; return rows; }

        double face = Math.Max(benchH * Math.Max(0, slope), Math.Max(1e-4, minFaceRun));
        double step = face + Math.Max(0, benchW);

        // 점마다 안쪽 법선 — 이웃 두 마디의 평균 방향에 직각으로, 부호는 <paramref name="inward"/>로 맞춘다
        int n = chain.Count;
        var nx = new double[n]; var ny = new double[n];
        for (int i = 0; i < n; i++)
        {
            double dx = 0, dy = 0;
            if (i > 0) { dx += chain[i].X - chain[i - 1].X; dy += chain[i].Y - chain[i - 1].Y; }
            if (i < n - 1) { dx += chain[i + 1].X - chain[i].X; dy += chain[i + 1].Y - chain[i].Y; }
            double L = Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-12) { nx[i] = 0; ny[i] = 0; continue; }
            double ax = -dy / L, ay = dx / L;
            if (ax * inward.X + ay * inward.Y < 0) { ax = -ax; ay = -ay; }
            nx[i] = ax; ny[i] = ay;
        }

        int dropped = 0;
        List<Point3>? Row(double off, double z)
        {
            var r = new List<Point3>(n);
            for (int i = 0; i < n; i++)
            {
                double x = chain[i].X + nx[i] * off, y = chain[i].Y + ny[i] * off;
                if (off > 1e-9 && !GradingGeometry.PointInRing(poly, x, y)) { dropped++; continue; }
                r.Add(new Point3(x, y, z));
            }
            return r.Count >= 2 ? r : null;
        }

        var r0 = Row(0, z0);
        if (r0 != null) rows.Add(r0);
        for (int k = 1; k <= benches; k++)
        {
            double zTop = z0 + benchH * k;
            var rf = Row(step * (k - 1) + face, zTop);            // 면 끝
            if (rf != null) rows.Add(rf);
            if (k < benches)                                      // 마지막 단은 <b>면에서 끝</b>
            {
                var rb = Row(step * k, zTop);                     // 소단 끝
                if (rb != null) rows.Add(rb);
            }
        }

        sb.Append($"단 <b>{benches}</b>개 · 줄 <b>{rows.Count}</b>개(한 단에 둘 · 마지막 단은 면에서 끝)");
        sb.Append($" · 면 {face:0.###}m + 소단 {Math.Max(0, benchW):0.##}m = 한 단 {step:0.###}m");
        sb.Append($" · 표고 {z0:F2} → <b>{z0 + benchH * benches:F2}m</b>");
        if (dropped > 0) sb.Append($" · 폴리곤 밖이라 <b>버린 점 {dropped}개</b>");
        log = sb.ToString();
        return rows;
    }

    /// <summary>★★[v102.2 · JACK 0930 «끝선을 꺾지 않고 연장선처럼 뺐는데 강제로 꺾여서 옹벽이 생겼다»] 손으로 그린 옹벽 폴리곤을 잇는다 — WallPolyDraw가 부른다.
    /// <para>측선(옹벽) = 구간 끝 → <b>찍은 방향 그대로</b> «데이라잇 + 여유» 테두리(<see cref="GradingGeometry.BufferedRingOutward"/>)에 처음 닿는 점까지.
    /// 두 닿은 점 사이는 그 테두리를 따라 두른다(폐합면 — 수직). 측선은 테두리 안에서 처음 밖으로 나가는 자리에서 끝나므로 두른 선이 측선을 가로지를 수 없다.</para>
    /// <para>종전(0917 S134 이음매 고침)은 데이라잇에 닿은 점에서 <b>테두리의 가장 가까운 점</b>(법선 쪽)을 모서리로 써 측선이 찍은 방향에서 틀어졌고,
    /// 측선이 옹벽이라 벽이 꺾였다. 새 길이 안 되면(테두리를 못 만듦 · 광선이 안 닿음 · 제 몸을 지름 · 넓이 &lt; 1㎡) <b>종전 모서리</b>로 물러나고 까닭을 적는다.</para>
    /// <para>★[v102.2 · 코드 검토 중간 1] <b>어느 쪽으로 두르나</b> — 테두리의 두 갈래는 측선 + 구간을 사이에 두고 반대쪽을 덮는다.
    /// 폴리곤은 구간의 <b>바깥쪽</b>(부지 반대쪽)에 서야 하므로, 두 갈래로 링을 다 이어 보고 <b>돌리는 방향</b>(넓이 부호)이 바깥쪽과 맞는 쪽을 쓴다.
    /// 종전(두 갈래의 «가운데 번호 점»이 구간 가운데에 가까운 쪽)은 좁은 부지에서 한 바퀴 도는 쪽을 골라 <b>부지 전체를 덮는</b> 폴리곤이 됐다
    /// (검토 재현: 폭 20m · 벽 100m — 짧은 쪽 점수 2725 · 한 바퀴 쪽 625). 바깥쪽을 모르면 종전 규칙으로 두르고 ⚠를 적는다.</para>
    /// <para>★[v102.2 · 재검토 중간 1] 바깥쪽은 <b>자 고리가 도는 방향</b>으로 받는다(<see cref="GradingGeometry.OutwardSideOfTravel"/> — 울타리 법선을 만드는
    /// <see cref="GradingGeometry.OutwardAt"/>과 같은 규칙). 끝 법선으로 가르던 첫 고침은 구간 끝(T1)이 자의 꼭짓점에 붙으면 그 법선이 <b>다음 변</b>의 것이라
    /// 못 갈랐다(사각 부지 한 변을 모서리에서 모서리까지 고르면 흔하다).</para></summary>
    /// <param name="seg">고른 구간 점(end0 → end1) — 자 위에서 T가 느는 쪽(<see cref="SegmentPoints"/>).</param>
    /// <param name="dirA">시점 방향(end1에서 찍은 쪽) · <paramref name="dirB"/> 종점 방향(end0에서).</param>
    /// <param name="outwardSide">바깥쪽이 구간 진행(end0 → end1)의 왼쪽이면 +1 · 오른쪽이면 −1 · 모르면 0(<see cref="GradingGeometry.OutwardSideOfTravel"/>).</param>
    /// <param name="dayA">시점 광선이 데이라잇에 닿은 점 · <paramref name="dayB"/> 종점 — 물러날 때(종전 길) 쓴다.</param>
    /// <param name="margin">데이라잇 바깥 여유(m).</param>
    /// <param name="spacing">측선 점 간격(m) — 구간 점 간격과 맞춘다(줄이 측선을 따라가게).</param>
    /// <param name="usedRay">참이면 찍은 방향 그대로 · 거짓이면 물러났다.</param>
    public static List<Point3>? AssembleManual(IReadOnlyList<Point3> seg, (double X, double Y) dirA, (double X, double Y) dirB, int outwardSide,
        Point3 dayA, Point3 dayB, IReadOnlyList<Point3> dayRing, double margin, double spacing, double z,
        out List<bool> isWall, out bool usedRay, out string note)
    {
        isWall = new List<bool>(); usedRay = false; note = "";
        if (seg == null || seg.Count < 2 || dayRing == null || dayRing.Count < 3) { note = "⚠구간 점·데이라잇이 모자라다"; return null; }
        var end0 = seg[0]; var end1 = seg[seg.Count - 1];
        var mid = seg[seg.Count / 2];
        double sp = Math.Max(0.1, spacing);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int ow = Math.Sign(outwardSide);

        // 링 하나를 잇는다 — 선택구간(옹벽) → 측선 A(옹벽) → 두른 선(수직 · 첫·끝 점은 측선 모서리) → 측선 B(옹벽)
        List<Point3> Build(Point3 cA, Point3 cB, IReadOnlyList<Point3> arc, List<bool> flags)
        {
            var ring = new List<Point3>();
            void Add(Point3 q, bool w)
            {
                if (ring.Count > 0 && Math.Abs(ring[^1].X - q.X) < 1e-9 && Math.Abs(ring[^1].Y - q.Y) < 1e-9) { if (w) flags[^1] = true; return; }
                ring.Add(new Point3(q.X, q.Y, z)); flags.Add(w);
            }
            void Side(Point3 pa, Point3 pb, bool skipFirst)
            {
                double dx = pb.X - pa.X, dy = pb.Y - pa.Y, L = Math.Sqrt(dx * dx + dy * dy);
                int n = Math.Max(1, (int)Math.Ceiling(L / sp));
                for (int i = skipFirst ? 1 : 0; i <= n; i++) Add(new Point3(pa.X + dx * i / n, pa.Y + dy * i / n, z), true);   // 측선 = 옹벽
            }
            foreach (var q in seg) Add(q, true);                                            // 선택구간 = 옹벽(end0 → end1)
            Side(end1, cA, true);                                                           // 측선(시점 쪽) — 모서리까지 옹벽
            foreach (var q in arc) Add(q, false);                                           // 두른 선 = 수직(첫 점은 모서리와 겹쳐 옹벽으로 남는다)
            Side(cB, end0, false);                                                          // 측선(종점 쪽) — 모서리부터 옹벽
            if (ring.Count > 1 && Math.Abs(ring[^1].X - ring[0].X) < 1e-9 && Math.Abs(ring[^1].Y - ring[0].Y) < 1e-9)
            { ring.RemoveAt(ring.Count - 1); flags.RemoveAt(flags.Count - 1); }             // 닫음점 중복 제거
            return ring;
        }
        static double Deg((double X, double Y) d, Point3 a, Point3 b)
        {
            double ux = b.X - a.X, uy = b.Y - a.Y, l1 = Math.Sqrt(ux * ux + uy * uy), l2 = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (l1 < 1e-12 || l2 < 1e-12) return 0;
            return Math.Acos(Math.Clamp((ux * d.X + uy * d.Y) / (l1 * l2), -1, 1)) * 180 / Math.PI;
        }
        // ★[v102.2 · 코드 검토 중간 1] 두 갈래로 링을 다 이어 보고 <b>돌리는 방향이 바깥쪽과 맞는</b> 쪽 — 못 가르면 종전 규칙(byScore)으로 두르고 까닭을 갈라 ⚠
        (List<Point3> Ring, List<bool> Flags, List<Point3> Arc, string Side) Pick(Point3 cA, Point3 cB, List<Point3> fw, List<Point3> bw, Func<List<Point3>> byScore)
        {
            string warn = "⚠바깥쪽을 몰라(자 고리가 도는 방향을 못 정했다)";
            if (ow != 0)
            {
                (List<Point3>, List<bool>, List<Point3>, string)? hit = null; int hits = 0;
                foreach (var arc in new[] { fw, bw })
                {
                    if (arc.Count < 2) continue;
                    var fl = new List<bool>(); var r = Build(cA, cB, arc, fl);
                    double sa = SignedArea(r);
                    if (Math.Abs(sa) >= 1.0 && Math.Sign(sa) == ow) { hits++; hit = (r, fl, arc, "바깥쪽으로 두름"); }
                }
                if (hits == 1) return hit!.Value;
                // ★[재검토 낮음 1] 까닭을 섞지 않는다 — 바깥쪽은 아는데 두 갈래가 둘 다(또는 둘 다 아니게) 맞으면 링이 꼬인 것이다
                warn = hits == 0 ? "⚠두 갈래 어느 쪽도 바깥쪽으로 돌지 않았다(링이 꼬였거나 넓이 1㎡ 미만)" : "⚠두 갈래가 둘 다 바깥쪽으로 돌았다(링이 꼬였다)";
            }
            var a0 = byScore();
            var f0 = new List<bool>();
            return (a0.Count >= 2 ? Build(cA, cB, a0, f0) : new List<Point3>(), f0, a0,
                    warn + " — 두 갈래 중 가운데가 구간에 가까운 쪽으로 둘렀다(종전 규칙 · 좁은 부지면 부지를 덮을 수 있다)");
        }

        // ① 새 길 — 찍은 방향으로 여유 테두리까지
        var why = new List<string>();
        var work = GradingGeometry.BufferedRingOutward(dayRing, margin);
        if (work == null || work.Count < 3) why.Add("여유 테두리를 못 만들었다");
        else
        {
            bool okA = GradingGeometry.RayRingHitInside(work, end1.X, end1.Y, dirA.X, dirA.Y, out double ax, out double ay, out double dA, 1e-6);
            bool okB = GradingGeometry.RayRingHitInside(work, end0.X, end0.Y, dirB.X, dirB.Y, out double bx, out double by, out double dB, 1e-6);
            if (!okA) why.Add("시점 방향이 여유 테두리에 안 닿는다");
            if (!okB) why.Add("종점 방향이 여유 테두리에 안 닿는다");
            if (okA && okB)
            {
                var cA = new Point3(ax, ay, z); var cB = new Point3(bx, by, z);
                int eA = GradingGeometry.RingEdgeOf(work, ax, ay), eB = GradingGeometry.RingEdgeOf(work, bx, by);
                var (fw, bw) = GradingGeometry.RingArcSides(work, ax, ay, eA, bx, by, eB);
                var pk = Pick(cA, cB, fw, bw, () => GradingGeometry.RingArcBetween(work, ax, ay, eA, bx, by, eB, mid.X, mid.Y));
                if (pk.Arc.Count < 2) why.Add("두 모서리 사이를 두르지 못했다");
                else
                {
                    var rR = pk.Ring;
                    bool simple = GradingGeometry.RingIsSimple(rR); double area = GradingGeometry.RingAreaNts(rR);
                    if (!simple) why.Add("제 몸을 지른다(두 측선이 엇갈렸나?)");
                    if (!(area >= 1.0)) why.Add(string.Format(ci, "넓이 {0:F2}㎡", area));
                    if (why.Count == 0)
                    {
                        usedRay = true; isWall = pk.Flags;
                        note = string.Format(ci, "측선 — <b>찍은 방향 그대로</b> 여유 테두리까지(시점 모서리 ({0:F1},{1:F1}) · 구간 끝에서 {2:F1}m · 종점 모서리 ({3:F1},{4:F1}) · {5:F1}m) · 두른 선 {6}점 · {7}",
                            ax, ay, dA, bx, by, dB, pk.Arc.Count, pk.Side);
                        // ★[계획 검토 v102.2 · 치명 1 부기] 데이라잇과 거의 나란히 찍으면 측선이 여유 테두리까지 길게 간다(여유/cosθ) — 알린다(막지는 않는다)
                        double gA = Math.Sqrt((ax - dayA.X) * (ax - dayA.X) + (ay - dayA.Y) * (ay - dayA.Y)), gB = Math.Sqrt((bx - dayB.X) * (bx - dayB.X) + (by - dayB.Y) * (by - dayB.Y));
                        if (Math.Max(gA, gB) > 3 * Math.Max(1.0, margin))
                            note += string.Format(ci, " · ⚠데이라잇 밖 측선이 시점 {0:F1}m · 종점 {1:F1}m로 길다(여유 {2:0.#}m의 3배 넘음 — 데이라잇과 거의 나란히 찍었다)", gA, gB, margin);
                        return rR;
                    }
                }
            }
        }
        // ② 물러남 — 종전 모서리(데이라잇에 닿은 점에서 테두리의 가장 가까운 꼭짓점 — RingArcOutward와 같은 모서리) · 두르는 쪽은 ①과 같이 바깥쪽
        var work0 = work ?? dayRing;
        static int Nearest(IReadOnlyList<Point3> r, double x, double y)
        {
            int bi = 0; double bd = double.MaxValue;
            for (int i = 0; i < r.Count; i++) { double dx = r[i].X - x, dy = r[i].Y - y, d2 = dx * dx + dy * dy; if (d2 < bd) { bd = d2; bi = i; } }
            return bi;
        }
        int i0 = Nearest(work0, dayA.X, dayA.Y), i1 = Nearest(work0, dayB.X, dayB.Y);
        if (i0 == i1) { note = "⚠폴리곤을 못 이었다 — " + string.Join(" · ", why) + " · 종전 길도 두를 선이 없다(두 모서리가 한 점)"; return null; }
        var oA = work0[i0]; var oB = work0[i1];
        var (fwO, bwO) = GradingGeometry.RingArcSides(work0, oA.X, oA.Y, i0, oB.X, oB.Y, i1);
        var pkO = Pick(new Point3(oA.X, oA.Y, z), new Point3(oB.X, oB.Y, z), fwO, bwO, () =>
        {
            var a = GradingGeometry.RingArcOutward(dayRing, dayA.X, dayA.Y, dayB.X, dayB.Y, mid.X, mid.Y, margin);
            if (a.Count >= 2 && (a[^1].X - dayA.X) * (a[^1].X - dayA.X) + (a[^1].Y - dayA.Y) * (a[^1].Y - dayA.Y)
                              < (a[0].X - dayA.X) * (a[0].X - dayA.X) + (a[0].Y - dayA.Y) * (a[0].Y - dayA.Y)) a.Reverse();
            return a;
        });
        if (pkO.Arc.Count < 2) { note = "⚠폴리곤을 못 이었다 — " + string.Join(" · ", why) + " · 종전 길도 두를 선이 없다"; return null; }
        var rO = pkO.Ring;
        isWall = pkO.Flags;
        note = string.Format(ci, "⚠측선을 <b>찍은 방향대로 못 그었다</b>({0}) — 종전처럼 데이라잇을 따라 두른 선의 끝 점 ({1:F1},{2:F1}) · ({3:F1},{4:F1})로 이었다(찍은 방향에서 시점 {5:F1}° · 종점 {6:F1}° 틀어짐) · {7}",
            string.Join(" · ", why), pkO.Arc[0].X, pkO.Arc[0].Y, pkO.Arc[^1].X, pkO.Arc[^1].Y, Deg(dirA, end1, pkO.Arc[0]), Deg(dirB, end0, pkO.Arc[^1]), pkO.Side);
        return GradingGeometry.RingIsSimple(rO) && GradingGeometry.RingAreaNts(rO) >= 1.0 ? rO : null;
    }

    /// <summary>★[v102.2 · 재검토 낮음 3 «검사 입력이 출하 입력이 아님»] 고른 구간 [t0, t1]을 자(고리) 위에서 1m 안팎 간격 점으로 뜬다(T가 느는 쪽 · 한 바퀴 넘김 대응).
    /// ZoneEditCommon이 손 폴리곤의 안쪽 변으로 쓰고, 하네스 S146이 <b>같은 함수</b>로 뜬다.</summary>
    public static List<Point3> SegmentPoints(IReadOnlyList<Point3> ruler, double[] cum, double t0, double t1)
    {
        double rTot = cum[cum.Length - 1];
        double span = t1 >= t0 ? t1 - t0 : rTot - t0 + t1;
        int nSeg = Math.Max(2, (int)Math.Ceiling(span / 1.0));
        var seg = new List<Point3>();
        for (int i = 0; i <= nSeg; i++)
        {
            double t = t0 + span * i / nSeg;
            seg.Add(GradingGeometry.PointAtParam(ruler, cum, ((t % rTot) + rTot) % rTot));
        }
        return seg;
    }

    /// <summary>★[v102.2 · 재검토 낮음 3] 자 위 <paramref name="t"/> 자리의 <b>울타리 법선</b>(바깥쪽 · 길이 1) — WallPolyDraw가 «노선 안쪽으로는 못 그린다»에 쓴다.
    /// <see cref="GradingGeometry.OutwardAt"/>이 T → T+0.5 접선으로 재므로 T가 꼭짓점이면 <b>다음 변</b>의 법선이다(구간 끝이 모서리면 연장선처럼 찍을 수 있는 까닭).</summary>
    public static (double X, double Y) FenceNormal(IReadOnlyList<Point3> ruler, double[] cum, double t)
    {
        double rTot = cum[cum.Length - 1];
        double tw = ((t % rTot) + rTot) % rTot;
        var o = GradingGeometry.PointAtParam(ruler, cum, tw);
        var q = GradingGeometry.OutwardAt(ruler, cum, tw, 1.0);
        return (q.X - o.X, q.Y - o.Y);
    }

    /// <summary>링의 넓이 부호(반시계 +) — 큰 좌표에서 자릿수를 잃지 않게 첫 점을 원점으로 옮겨 잰다.</summary>
    private static double SignedArea(IReadOnlyList<Point3> r)
    {
        if (r.Count < 3) return 0;
        double x0 = r[0].X, y0 = r[0].Y, a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            a += (r[j].X - x0) * (r[i].Y - y0) - (r[i].X - x0) * (r[j].Y - y0);
        return a * 0.5;
    }
}
