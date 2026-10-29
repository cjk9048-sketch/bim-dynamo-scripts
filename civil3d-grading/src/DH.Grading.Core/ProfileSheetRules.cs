namespace DH.Grading.Core;

/// <summary>★★[v110.4 · 계획_v110_4.md 2.A · 7 M1 · H1] 진입도로 종단도(부지 [종단도]와 같은 꼴 — A1 도곽 · 밴드 6칸)의 규칙 — 화면 없는 시험(S180)이 본다.
/// <para>한 도면 = 한 축척: 밴드 칸 · 글자는 <b>종이 × 도면 주석 축척</b>이고 축 간격은 <b>공유 뷰 스타일</b>에 있다 —
/// 도로와 부지 축척이 다르면 나중 것이 먼저 것을 틀어 놓는다(계획 0-1). 그래서 부지 종단도가 있으면 도로는 그 축척을 따른다.</para></summary>
public static class ProfileSheetRules
{
    /// <summary>A1 가로(종이 mm) — <c>SheetCommand.SheetW</c>와 같은 값.</summary>
    public const double SheetWmm = 841.0;

    /// <summary>★[계획 3.4] 부지 종단도도 고정 축척도 없을 때 — 1:500(한 장 746 mm × 500 = 373 m · 축 5 m = 10 mm로 템플릿 그대로).</summary>
    public const double DefaultScale = 500.0;

    /// <summary>★★[계획 검토 M1] 도로 종단도 축척.
    /// <list type="number">
    /// <item>도로가 아닌 DH 종단뷰가 있으면(<paramref name="siteView"/>) = 부지 축척 — 도면 주석 축척(<paramref name="siteCanno"/> · DH 규약일 때만 0 아님)
    /// · 0이면 부지 도곽 사각형 폭(<paramref name="siteFrameW"/> 모형 m) ÷ 841 mm를 사다리에 붙임. 고정 축척이 다르면 알림만.</item>
    /// <item>부지 종단뷰가 없으면 도면설정 고정 축척(<paramref name="pinned"/> &gt; 0) · 아니면 <paramref name="fallback"/>(1:500).</item>
    /// </list>
    /// 부지 종단뷰가 있는데 둘 다 못 읽으면(0) 2번으로 물러난다.</summary>
    public static (double Scale, string Note) RoadScale(bool siteView, double siteCanno, double siteFrameW, double pinned,
                                                        IReadOnlyList<double>? ladder, double fallback = DefaultScale)
    {
        bool okPin = double.IsFinite(pinned) && pinned > 0;
        if (siteView)
        {
            bool byCanno = double.IsFinite(siteCanno) && siteCanno > 0;
            double s = byCanno ? siteCanno
                     : double.IsFinite(siteFrameW) && siteFrameW > 0 ? Snap(siteFrameW * 1000.0 / SheetWmm, ladder) : 0;
            if (s > 0)
            {
                string note = $"부지 종단도 축척 1:{s:0} 따름({(byCanno ? "도면 주석 축척" : "부지 도곽 폭")})";
                if (okPin && Math.Abs(pinned - s) > 1e-6) note += $" · ⚠도면설정 고정 1:{pinned:0}과 다름 — 한 도면 한 축척이라 부지 축척으로";
                return (s, note);
            }
        }
        if (okPin) return (pinned, $"도면설정 고정 1:{pinned:0}");
        double f = double.IsFinite(fallback) && fallback > 0 ? fallback : DefaultScale;
        return (f, $"기본 1:{f:0}(부지 종단도 · 고정 축척 없음)");
    }

    /// <summary>사다리에서 가장 가까운 값(같은 거리면 앞 = 작은 값). 사다리가 비면 그대로.</summary>
    public static double Snap(double v, IReadOnlyList<double>? ladder)
    {
        if (ladder == null || ladder.Count == 0 || !double.IsFinite(v)) return v;
        double best = ladder[0];
        foreach (double q in ladder) if (Math.Abs(q - v) < Math.Abs(best - v) - 1e-9) best = q;
        return best;
    }

    /// <summary>★[계획 2.A · 검토 L4] 도로 검토선 좌우 폭 — 가장자리(차로 + 길어깨) + 그쪽 가장 넓은 사면 + 여유(5 m)를 5 m 단위로 올림.
    /// <para>부지의 <c>XsecWidth.Resolve</c>는 횡단 폭 설정을 덮으므로 도로 길에서 부르지 않는다 — 이것이 대신한다.</para></summary>
    public static (double L, double R) RoadWidth(double outerL, double outerR, double slopeL, double slopeR, double margin = 5, double step = 5)
    {
        double stp = double.IsFinite(step) && step > 0 ? step : 5;
        double One(double o, double s) => Math.Max(stp, Math.Ceiling((Pos(o) + Pos(s) + Pos(margin)) / stp - 1e-9) * stp);
        return (One(outerL, slopeL), One(outerR, slopeR));
    }

    static double Pos(double v) => double.IsFinite(v) && v > 0 ? v : 0;

    /// <summary>★[계획 3.3] 종단도를 그린 때의 계획 종단 도장 — PVI(측점 · 높이)와 노선 시 · 종점을 mm로 반올림해 FNV-1a 64비트.
    /// 지금 도장과 다르면 «종단도가 지금 종단과 다릅니다 — [종단도 다시]» (밴드 값은 Civil이 따라가지만 측점 · 표고 범위 · 글씨 벌리기는 그대로라서).</summary>
    public static string Stamp(IEnumerable<(double S, double Z)>? pvi, double s0, double s1)
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; }
        }
        static long Mm(double v) => double.IsFinite(v) ? (long)Math.Round(v * 1000.0) : long.MinValue;
        Mix(Mm(s0)); Mix(Mm(s1));
        long n = 0;
        if (pvi != null) foreach (var (s, z) in pvi) { Mix(Mm(s)); Mix(Mm(z)); n++; }
        Mix(n);
        return h.ToString("x16");
    }

    /// <summary>★★[계획 4 · 검토 M3] 지우기 판정 — 부지 길 = 표식이 없는 것만(지금 조건 그대로 · 도로 것은 안 지움) ·
    /// 도로 길 = <b>이 노선 표식</b>이 있는 것만(표식 없는 것 · 다른 도로 것은 안 지움).</summary>
    /// <param name="tagAlign">개체의 <c>DH_ROADSHEET</c> 표식 노선 핸들 — 표식이 없으면 null.</param>
    public static bool ShouldErase(bool roadPath, string? tagAlign, string? myAlign)
        => roadPath ? !string.IsNullOrEmpty(tagAlign) && !string.IsNullOrEmpty(myAlign)
                      && string.Equals(tagAlign, myAlign, StringComparison.OrdinalIgnoreCase)
                    : tagAlign == null;

    /// <summary>모형 사각형(도곽).</summary>
    public readonly record struct Rect(double X0, double Y0, double X1, double Y1)
    {
        public bool Contains(double x, double y, double tol = 1e-6)
            => x >= Math.Min(X0, X1) - tol && x <= Math.Max(X0, X1) + tol
            && y >= Math.Min(Y0, Y1) - tol && y <= Math.Max(Y0, Y1) + tol;
    }

    /// <summary>★★[계획 검토 H1] 부지 길의 밴드 라벨 묶음 — 묶음 평균 (X, Y)가 도로 도곽(<c>DH_ROADSHEET</c> 표식 사각형) 안이면 부지 것이 아니다.
    /// 도로 도곽이 없으면 늘 참(부지만 있는 도면은 그대로).</summary>
    public static bool SiteOwnsGroup(double cx, double cy, IEnumerable<Rect>? roadRects)
    {
        if (roadRects != null) foreach (var r in roadRects) if (r.Contains(cx, cy)) return false;
        return true;
    }

    /// <summary>★★[계획 검토 H1] 도로 길의 밴드 라벨 묶음 — 그 뷰의 밴드 상자(X 범위 × [그래프 아래 끝 − 밴드 모형 높이, 그래프 아래 끝]) 안만.</summary>
    public static bool RoadOwnsGroup(double cx, double cy, double xLo, double xHi, double yGraphBottom, double bandH, double tol)
    {
        double t = double.IsFinite(tol) && tol > 0 ? tol : 0;
        double bh = double.IsFinite(bandH) && bandH > 0 ? bandH : 0;
        return cx >= Math.Min(xLo, xHi) - t && cx <= Math.Max(xLo, xHi) + t
            && cy <= yGraphBottom + t && cy >= yGraphBottom - bh - t;
    }

    /// <summary>두 가로 범위가 겹치나(맞닿기만 하면 안 겹침).</summary>
    public static bool SpansOverlap(double a0, double a1, double b0, double b1)
        => Math.Max(Math.Min(a0, a1), Math.Min(b0, b1)) < Math.Min(Math.Max(a0, a1), Math.Max(b0, b1)) - 1e-9;

    /// <summary>★[계획 검토 H1] 도로 종단도 <paramref name="sheets"/>장이 쓸 가로 범위(모형 m) — 놓는 점(첫 뷰 삽입점)에서
    /// 왼쪽 축 · 배너 · 도곽 여백(종이 130 mm 넉넉히) · 오른쪽 장 수 × 841 mm.</summary>
    public static (double X0, double X1) RoadSheetsSpan(double atX, double scale, int sheets)
    {
        double s = (double.IsFinite(scale) && scale > 0 ? scale : DefaultScale) / 1000.0;
        int n = Math.Max(1, sheets);
        return (atX - 130.0 * s, atX + n * SheetWmm * s);
    }
}
