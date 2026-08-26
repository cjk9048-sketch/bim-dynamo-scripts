namespace DH.Grading.Core;

/// <summary>수량표의 <b>한 줄</b>. 세 칸(항목·세부·재료)과 값 칸으로 이뤄진다.
/// <para><b>병합은 "몇 줄을 먹느냐"로 적는다.</b> <c>Rows=0</c>이면 <b>위 줄에 흡수된다</b> —
/// 도면의 세로 병합이 그 모양이다. <c>Sub</c>이 <c>null</c>이면 항목 칸이 <b>세부 칸까지 가로로</b> 넓어진다.</para></summary>
/// <param name="Name">1열 — 항목. 줄바꿈은 <c>|</c>로 적는다(예: <c>터 파 기|(5.0m이하)</c>).</param>
/// <param name="NameRows">1열이 먹는 줄 수. <b>0이면 위 줄과 병합</b>.</param>
/// <param name="Sub">2열 — 세부. <c>null</c>이면 1열이 2열까지 넓어진다.</param>
/// <param name="SubRows">2열이 먹는 줄 수. <b>0이면 위 줄과 병합</b>.</param>
/// <param name="Material">3열 — 재료(토사·풍화암).</param>
public readonly record struct QtyRow(string Name, int NameRows, string Sub, int SubRows, string Material);

/// <summary>★[JACK 0826] <b>토공 횡단면도에 붙는 수량표.</b>
///
/// <para>JACK이 준 실제 도면(산외배수지 토공 횡단면도)을 그대로 옮겼다 —
/// 머리 1줄 + 내용 <b>18줄</b>. 값은 아직 비워 둔다(<c>–</c>): <i>"수량 구하는 건 미루고
/// 일단 표만 같이 배치해서 레이아웃부터 잡자"</i>.</para>
///
/// <para><b>왜 Core에 두나.</b> 표의 <b>모양</b>은 도면이 없어도 정해지는 순수한 데이터다.
/// 여기 있으면 오프라인 하니스가 줄 수·병합 합계를 직접 잴 수 있다 —
/// 화면에서만 확인되는 규칙은 언젠가 조용히 어긋난다(이 프로젝트가 여러 번 겪었다).</para></summary>
public static class QuantityTable
{
    /// <summary>값이 아직 없을 때 칸에 적는 글자. 도면에서도 <c>–</c>로 비워 둔다.</summary>
    public const string Blank = "–";

    /// <summary>머리줄 왼쪽 — 오른쪽 칸에는 측점 이름(<c>NO. 0+5.000</c>)이 들어간다.</summary>
    public const string HeaderLeft = "측  점";

    /// <summary>토공 수량 18줄. <b>순서가 곧 도면 순서</b>이고, 나중에 수량을 계산할 때도 이 순서를 쓴다.</summary>
    public static readonly QtyRow[] Rows =
    {
        new("절  토",              1, "육  상", 1, "토  사"),
        new("터 파 기|(5.0m이하)", 3, "육  상", 1, "토  사"),
        new("",                    0, "용  수", 2, "토  사"),
        new("",                    0, "",       0, "풍화암"),
        new("터 파 기|(5.0m이상)", 3, "육  상", 1, "토  사"),
        new("",                    0, "용  수", 2, "토  사"),
        new("",                    0, "",       0, "풍화암"),
        new("되메우기",            1, null,     0, "토  사"),
        new("성    토",            1, null,     0, "토  사"),
        new("구조물 피복토",       1, null,     0, "토  사"),
        new("바닥면 고르기",       1, null,     0, "풍화암"),
        new("면고르기",            2, "성토면", 1, "토  사"),
        new("",                    0, "절토면", 1, "토  사"),
        new("비탈면|보호공",       2, "성토부", 1, "토  사"),
        new("",                    0, "절토부", 1, "토  사"),
        new("표토제거",            1, null,     0, "토  사"),
        new("벌개제근",            1, null,     0, "토  사"),
        new("층따기",              1, null,     0, "토  사"),
    };

    /// <summary>머리줄을 뺀 내용 줄 수.</summary>
    public static int BodyRows => Rows.Length;

    /// <summary>머리줄까지 넣은 전체 줄 수 — 표 높이를 잡을 때 쓴다.</summary>
    public static int TotalRows => Rows.Length + 1;

    /// <summary>병합이 앞뒤가 맞는지 — <b>1열이 먹겠다고 한 줄 수의 합</b>이 전체 줄 수와 같아야 한다.
    /// 어긋나면 표가 어긋난 채로 그려지는데, 도면에서는 한참 뒤에야 눈에 띈다.</summary>
    public static bool NameSpansValid()
    {
        int sum = 0;
        foreach (var r in Rows) sum += r.NameRows;
        return sum == Rows.Length;
    }

    /// <summary>2열도 같은 방식으로 — 다만 <c>Sub</c>이 <c>null</c>인 줄은 2열 자체가 없으므로 1줄로 센다.</summary>
    public static bool SubSpansValid()
    {
        int sum = 0;
        foreach (var r in Rows) sum += r.Sub == null ? 1 : r.SubRows;
        return sum == Rows.Length;
    }

    /// <summary>★★[JACK 0826 "먼저 절토, 터파기, 되메우기, 성토만 진행하고 나머지는 빈 공간으로"]
    /// <b>어느 줄에 어떤 수량이 들어가는가.</b>
    ///
    /// <para>줄 번호(0부터)와 <see cref="XsecQty"/>의 어느 값인지를 짝지어 둔다.
    /// <b>표 모양 바로 옆에 두는 이유</b>: 줄을 추가·삭제할 때 이 짝도 같이 눈에 들어와야 한다.
    /// 다른 파일에 흩어 두면 줄 하나 옮겼을 때 값이 엉뚱한 칸에 들어간다.</para>
    ///
    /// <para>여기 없는 줄은 <b>빈칸</b>이다 — 아직 계산하지 않는 공종이다.
    /// 0으로 채우면 "재 봤더니 없다"로 읽혀 잘못이다.</para></summary>
    public enum QtyKind { None, Cut, Fill, ExcShallow, ExcDeep, Backfill }

    /// <summary>줄마다 어떤 값이 들어가는지 — <see cref="Rows"/>와 <b>같은 차례</b>다.</summary>
    public static readonly QtyKind[] RowKind =
    {
        QtyKind.Cut,          // 0  절토 · 육상 · 토사
        QtyKind.ExcShallow,   // 1  터파기(5.0m이하) · 육상 · 토사
        QtyKind.None,         // 2  터파기(5.0m이하) · 용수 · 토사      ← 지하수위가 없어 못 가른다
        QtyKind.None,         // 3  터파기(5.0m이하) · 용수 · 풍화암    ← 지층이 없어 못 가른다
        QtyKind.ExcDeep,      // 4  터파기(5.0m이상) · 육상 · 토사
        QtyKind.None,         // 5  터파기(5.0m이상) · 용수 · 토사
        QtyKind.None,         // 6  터파기(5.0m이상) · 용수 · 풍화암
        QtyKind.Backfill,     // 7  되메우기
        QtyKind.Fill,         // 8  성토
        QtyKind.None,         // 9  구조물 피복토
        QtyKind.None,         // 10 바닥면 고르기
        QtyKind.None,         // 11 면고르기 · 성토면
        QtyKind.None,         // 12 면고르기 · 절토면
        QtyKind.None,         // 13 비탈면 보호공 · 성토부
        QtyKind.None,         // 14 비탈면 보호공 · 절토부
        QtyKind.None,         // 15 표토제거
        QtyKind.None,         // 16 벌개제근
        QtyKind.None,         // 17 층따기
    };

    /// <summary>줄 수와 짝 수가 맞는지 — 어긋나면 값이 <b>한 칸씩 밀린다</b>.</summary>
    public static bool RowKindValid() => RowKind.Length == Rows.Length;

    /// <summary>그 줄에 넣을 값을 고른다. 없으면 <c>NaN</c>.</summary>
    public static double Pick(XsecQty q, int row)
    {
        if (row < 0 || row >= RowKind.Length) return double.NaN;
        return RowKind[row] switch
        {
            QtyKind.Cut => q.Cut,
            QtyKind.Fill => q.Fill,
            QtyKind.ExcShallow => q.ExcShallow,
            QtyKind.ExcDeep => q.ExcDeep,
            QtyKind.Backfill => q.Backfill,
            _ => double.NaN,
        };
    }
}
