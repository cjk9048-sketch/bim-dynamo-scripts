namespace DH.Grading.Core;

/// <summary>★★★[JACK 0827 스크린샷] <b>토적표 — 두 단으로 접은 새 형태.</b>
///
/// <para><b>왜 바꿨나.</b> 종전은 <b>18줄 한 단</b>이라 세로로 길쭉했다. 새 형태는 같은 항목을
/// <b>12줄 두 단</b>으로 접어 가로로 눕힌다 — 횡단면도 옆에 붙이기에 그 편이 맞다.</para>
///
/// <para><b>왼쪽 12줄과 오른쪽 12줄이 정확히 대응한다.</b> 그래서 표는 언제나 직사각형이고,
/// 병합 계산이 어긋날 자리가 없다.</para>
///
/// <code>
/// ┌─────────────────────────────────────────────────────────┐
/// │ 측  점 :  No.2+13.92                                    │
/// ├──────┬──────┬──────┬───────┬──────────┬──────┬─────────┤
/// │ 성  토      │ 토 사 │ 49.98 │ 벌개재근 │성토부│  16.99  │
/// │ 절  토      │ 토 사 │ 44.48 │          │절토부│  22.32  │
/// │             │ 풍화암│  1.68 │ 표토제거 │성토부│    –    │
/// │             │ 연 암 │  0.53 │          │절토부│    –    │
/// │ 4.5m │터파기│ 토 사 │  5.39 │ 면고르기 │성토부│    –    │
/// │ 이하 │(육상)│ 풍화암│   –   │          │절토부│  14.66  │
/// │      │      │ 연 암 │   –   │바닥면고르기│풍화암│  –    │
/// │      │터파기│ 토 사 │   –   │          │연 암 │    –    │
/// │      │(용수)│ 풍화암│   –   │ 식생공법 │성토부│    –    │
/// │      │      │ 연 암 │   –   │          │절토부│    –    │
/// │ 되메우기    │ 구조물│  0.58 │ 층  따  기      │  5.03   │
/// │             │ 주 위 │   –   │ 잡 석 부 설     │    –    │
/// └──────┴──────┴──────┴───────┴──────────┴──────┴─────────┘
/// </code>
///
/// <para><b>열은 일곱이다.</b> 왼쪽 넷(대분류·중분류·재료·값) + 오른쪽 셋(항목·세부·값).</para></summary>
public static class QuantityTable
{
    /// <summary>값이 없는 칸에 적는 글자. <b>빈칸으로 두지 않는다</b> —
    /// 빈칸은 "아직 안 넣었다"로 읽히고, <c>–</c>는 "해당 없음"으로 읽힌다(JACK 지시).</summary>
    public const string Blank = "–";

    /// <summary>표 맨 윗줄. 측점 이름이 뒤에 붙는다.</summary>
    public const string HeaderLeft = "측  점";

    /// <summary>본문 줄 수 — 왼쪽과 오른쪽이 같다.</summary>
    public const int BodyRows = 12;

    /// <summary>머리 한 줄을 더한 전체 줄 수.</summary>
    public const int TotalRows = BodyRows + 1;

    /// <summary>가로 칸 수.</summary>
    public const int Cols = 7;

    /// <summary>어느 값을 넣을 자리인가. <c>None</c>은 <b>지금은 못 구하는 것</b>이다 —
    /// 지층·지하수위 자료가 들어오면 그때 채운다.</summary>
    public enum QtyKind { None, Cut, Fill, ExcShallow, ExcDeep, Backfill }

    /// <summary>한 칸. <paramref name="RowSpan"/>이 0이면 <b>위 칸에 먹힌 자리</b>다.</summary>
    public readonly record struct Cell(string Text, int RowSpan = 1, int ColSpan = 1);

    /// <summary>본문 한 줄 — 왼쪽 세 칸 + 왼쪽 값 + 오른쪽 두 칸 + 오른쪽 값.</summary>
    public readonly record struct Row(
        Cell L1, Cell L2, Cell L3, QtyKind LKind,
        Cell R1, Cell R2, QtyKind RKind);

    private static Cell C(string t, int rs = 1, int cs = 1) => new(t, rs, cs);
    private static readonly Cell Eaten = new(null, 0, 0);   // 위 칸이 먹은 자리

    /// <summary>★[JACK 0827] 터파기 깊이 구분. 표 제목에 그대로 쓰인다.
    /// <para>스크린샷은 <b>4.5m</b>였다. 계산 쪽(<c>XsecQuantity.DeepLimit</c>)과 <b>같은 값</b>이라야
    /// 표에 적힌 글자와 실제 가른 깊이가 어긋나지 않는다.</para></summary>
    public const double DeepLimitM = 5.0;

    /// <summary>깊이 구분 글자 — 값이 바뀌면 따라 바뀐다. <b>글자를 못 박지 않는다.</b></summary>
    public static string DepthLabel => $"{DeepLimitM:0.#}m|이하";

    public static readonly Row[] Rows =
    {
        // 0
        new(C("성    토", 1, 2), Eaten,              C("토  사"), QtyKind.Fill,
            C("벌개재근", 2),    C("성토부"),        QtyKind.None),
        // 1
        new(C("절    토", 3, 2), Eaten,              C("토  사"), QtyKind.Cut,
            Eaten,               C("절토부"),        QtyKind.None),
        // 2
        new(Eaten,               Eaten,              C("풍화암"), QtyKind.None,
            C("표토제거", 2),    C("성토부"),        QtyKind.None),
        // 3
        new(Eaten,               Eaten,              C("연  암"), QtyKind.None,
            Eaten,               C("절토부"),        QtyKind.None),
        // 4
        new(C(null, 6),          C("터파기|(육상)", 3), C("토  사"), QtyKind.ExcShallow,
            C("면고르기", 2),    C("성토부"),        QtyKind.None),
        // 5
        new(Eaten,               Eaten,              C("풍화암"), QtyKind.None,
            Eaten,               C("절토부"),        QtyKind.None),
        // 6
        new(Eaten,               Eaten,              C("연  암"), QtyKind.None,
            C("바닥면고르기", 2), C("풍화암"),       QtyKind.None),
        // 7
        new(Eaten,               C("터파기|(용수)", 3), C("토  사"), QtyKind.None,
            Eaten,               C("연  암"),        QtyKind.None),
        // 8
        new(Eaten,               Eaten,              C("풍화암"), QtyKind.None,
            C("식생공법", 2),    C("성토부"),        QtyKind.None),
        // 9
        new(Eaten,               Eaten,              C("연  암"), QtyKind.None,
            Eaten,               C("절토부"),        QtyKind.None),
        // 10
        new(C("되메우기", 2, 2), Eaten,              C("구조물"), QtyKind.None,
            C("층 따 기", 1, 2), Eaten,              QtyKind.None),
        // 11
        new(Eaten,               Eaten,              C("주  위"), QtyKind.Backfill,
            C("잡 석 부 설", 1, 2), Eaten,           QtyKind.None),
    };

    /// <summary>왼쪽 대분류(0열)에서 <see cref="DepthLabel"/>이 들어갈 줄 — 4번이다.
    /// <para>글자를 배열에 직접 못 박으면 깊이를 바꿀 때 <b>두 곳을 고쳐야</b> 하므로 여기서 끼워 넣는다.</para></summary>
    public const int DepthRow = 4;

    /// <summary>그 줄의 0열 글자 — <see cref="DepthRow"/>만 깊이 딱지를 돌려준다.</summary>
    public static string L1TextOf(int row)
    {
        if (row < 0 || row >= Rows.Length) return null;
        if (row == DepthRow) return DepthLabel;
        return Rows[row].L1.Text;
    }

    /// <summary>세로 병합의 합이 줄 수와 맞는가 — 어긋나면 표가 찌그러진다.</summary>
    public static bool SpansValid()
    {
        int l1 = 0, l2 = 0, l3 = 0, r1 = 0, r2 = 0;
        foreach (var r in Rows)
        {
            l1 += r.L1.RowSpan; l2 += r.L2.RowSpan; l3 += r.L3.RowSpan;
            r1 += r.R1.RowSpan; r2 += r.R2.RowSpan;
        }
        // L1·L2는 서로 먹고 먹히므로 둘을 합쳐서 본다(2열 병합이 섞인다).
        return l3 == BodyRows && r1 + r2 == BodyRows * 2 - CountColSpan2Right()
            && l1 + l2 == BodyRows * 2 - CountColSpan2Left();
    }

    private static int CountColSpan2Left()
    {
        int n = 0;
        foreach (var r in Rows) if (r.L1.ColSpan == 2) n += r.L1.RowSpan;
        return n;
    }

    private static int CountColSpan2Right()
    {
        int n = 0;
        foreach (var r in Rows) if (r.R1.ColSpan == 2) n += r.R1.RowSpan;
        return n;
    }

    /// <summary>왼쪽 값 — 그 줄이 어느 수량을 받는가.</summary>
    public static double PickLeft(XsecQty q, int row) => Pick(q, row < 0 || row >= Rows.Length ? QtyKind.None : Rows[row].LKind);

    /// <summary>오른쪽 값.</summary>
    public static double PickRight(XsecQty q, int row) => Pick(q, row < 0 || row >= Rows.Length ? QtyKind.None : Rows[row].RKind);

    private static double Pick(XsecQty q, QtyKind k) => k switch
    {
        QtyKind.Cut => q.Cut,
        QtyKind.Fill => q.Fill,
        QtyKind.ExcShallow => q.ExcShallow,
        QtyKind.ExcDeep => q.ExcDeep,
        QtyKind.Backfill => q.Backfill,
        _ => double.NaN,
    };

    /// <summary>지금 실제로 값이 들어가는 자리 수 — 나머지는 <c>–</c>다.
    /// <para>자료(지층·지하수위)가 들어오면 이 수가 늘어난다. 로그에 남겨 진행을 눈으로 본다.</para></summary>
    public static int FilledSlots()
    {
        int n = 0;
        foreach (var r in Rows)
        {
            if (r.LKind != QtyKind.None) n++;
            if (r.RKind != QtyKind.None) n++;
        }
        return n;
    }

    /// <summary>가로 칸 폭의 비율. ★[JACK 0827 "표가 너무 넓어"]
    /// <para>종전 합 70은 <b>폭 252mm</b>가 되어 그림 자리를 크게 잡아먹었고 표가 납작해 보였다.
    /// 원본 스크린샷의 가로:세로가 <b>1.4:1</b>인데 우리는 3.4:1이었다.
    /// 합을 <b>38</b>로 줄여 폭 137mm로 만든다 — 13줄 × 7.4mm = 96mm이므로 1.43:1이 된다.</para>
    /// <para>★[검토 0828 · LOW-5] <b>위 숫자는 0827 당시의 것이다.</b>
    /// 0828에 좌우 짝 폭을 맞추며 합이 <b>44.6</b>(폭 161mm · 약 1.62:1)이 됐다 —
    /// <b>한 주석 안에 두 숫자가 살면 어느 것이 지금인지 알 수 없다</b>. 아래 0828 주석이 정본이다.</para>
    /// <para>글자가 들어가는지도 봤다: 가장 긴 글자는 <c>바닥면고르기</c>(6자)로
    /// 7.5 × 3.6mm = 27mm 자리에 15mm면 되므로 넉넉하다.</para></summary>
    //   ★[JACK 0827 실측] <b>글자가 접히는 칸을 넓혔다.</b> 접힌 것은 넷이다 —
    //   <c>성토부</c>·<c>절토부</c>·<c>풍화암</c>(5번 칸)과 <c>바닥면고르기</c>(4번 칸).
    //   Table은 칸마다 <b>자체 여백</b>을 두므로 글자 폭만 계산해선 모자란다 — 재 보고 넉넉히 준다.
    //
    // ★★★[JACK 0828 "A열+B열과 E열, C열과 F열, D열과 G열의 폭이 같아야 해"]
    //   <b>표는 좌우 두 짝이 마주 보는 구조다.</b> 왼쪽은 <c>이름칸(A+B) · 지층(C) · 값(D)</c>,
    //   오른쪽은 <c>공종(E) · 구분(F) · 값(G)</c> — 짝끼리 폭이 다르면 가운데 세로선이
    //   어긋나 보인다. 종전 값은 짝마다 조금씩 어긋나 있었다(9.1↔9.5 · 4.6↔6.2 · 6.1↔6.6).
    //   → <b>짝마다 넓은 쪽으로 맞춘다.</b> 좁은 쪽으로 맞추면 글자가 접히는데,
    //   접힌 글자는 이미 한 번 겪은 함정이다(바로 위 0827 주석).
    //   <c>A</c>·<c>B</c>는 합이 <c>E</c>와 같기만 하면 되므로 종전 비율(4.1:5.0)대로 나눈다.
    //   합은 42.1 → <b>44.6</b>(폭 152 → 161mm). 짝을 맞추는 값이라 <see cref="WidthsPaired"/>가 지킨다.
    public static readonly double[] ColRatio = { 4.3, 5.2, 6.2, 6.6, 9.5, 6.2, 6.6 };

    /// <summary>★★★[JACK 0828] <b>좌우 짝의 폭이 같은가</b> — A+B=E · C=F · D=G.
    /// <para>사람이 눈으로 지킬 규칙이 아니다. <see cref="ColRatio"/>를 한 자리라도 고치면
    /// 짝이 깨질 수 있으므로 <b>그리는 쪽이 매번 물어보고 로그에 남긴다</b>.
    /// (<see cref="SpansValid"/>는 만들어 놓고 <b>아무 데서도 안 불렀다</b> — 같은 실수를 반복하지 않는다.)</para></summary>
    public static bool WidthsPaired(out string note)
    {
        double ab = ColRatio[0] + ColRatio[1], e = ColRatio[4];
        double c = ColRatio[2], f = ColRatio[5];
        double d = ColRatio[3], g = ColRatio[6];
        const double Tol = 1e-9;
        bool ok1 = System.Math.Abs(ab - e) < Tol, ok2 = System.Math.Abs(c - f) < Tol,
             ok3 = System.Math.Abs(d - g) < Tol;
        note = $"A+B {ab:0.##}{(ok1 ? "=" : "≠")}E {e:0.##}"
             + $" · C {c:0.##}{(ok2 ? "=" : "≠")}F {f:0.##}"
             + $" · D {d:0.##}{(ok3 ? "=" : "≠")}G {g:0.##}";
        return ok1 && ok2 && ok3;
    }
}
