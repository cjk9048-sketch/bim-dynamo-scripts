using System;
using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0828] <b>수량 분류 — 표준시장단가가 가르는 다섯 가지.</b>
/// <para>JACK: <i>"우리가 적용한 것 토사·풍화암·연암·보통암·경암 5가지로만 나눔.
/// 보링 자료 입력 시 5가지 중 고르게."</i></para>
/// <para><b>층 이름과 수량 분류는 다르다.</b> 사용자는 층에 <c>표토</c>·<c>매립토</c>·<c>퇴적층</c>처럼
/// 조사보고서의 말을 그대로 붙이되, 그 층이 <b>수량으로는 무엇인지</b>를 이 다섯 중에서 고른다.
/// 그래야 이름이 현장마다 달라도 표는 늘 같은 자로 선다.</para></summary>
public enum RockClass
{
    Soil,       // 토  사
    Weathered,  // 풍화암
    Soft,       // 연  암
    Medium,     // 보통암
    Hard,       // 경  암
}

/// <summary>터파기 깊이 구분. ★<b>"5m 이상"이 아니라 "5m 초과"</b>다(JACK) —
/// <c>5.00m</c>는 이하에 들어가고 <c>5.01m</c>부터 초과다. 표에 적는 글자도 그래야 한다.</summary>
public enum DepthClass
{
    Le,   // 5m 이하
    Gt,   // 5m 초과
}

/// <summary>육상 / 용수. 표준시장단가가 계수를 달리 매기는 자리다(육상 1.00 · 용수 1.33).</summary>
public enum WaterClass
{
    Land,   // 육상
    Water,  // 용수
}

/// <summary>★★★[JACK 0828] <b>표는 현장의 지층 구성에서 만들어진다 — 못 박지 않는다.</b>
///
/// <para>JACK: <i>"도킹바에서 인식된 지층들을 자동으로 인식하고 표를 만들어야 해."</i></para>
///
/// <para><b>★ 표 모양은 도면 전체에서 하나다.</b> 측점마다 나오는 암종이 달라도
/// 줄 목록은 <b>현장의 지층 구성</b>으로 한 번 정한다 — 측점마다 줄 수가 달라지면
/// <b>횡단면도마다 표 높이가 달라져 축척이 제각각</b>이 된다.
/// (축척은 <c>TotalRows</c>를 읽어 정해진다 — 그 값이 흔들리면 도면이 흔들린다.)</para>
///
/// <para>그 측점에 없는 암종은 <b>줄은 있고 값이 <c>–</c></b>다 — 이 저장소의 규칙 그대로:
/// 빈칸은 "아직 안 넣었다", <c>–</c>는 "해당 없음".</para></summary>
public sealed class QtyTableSpec
{
    /// <summary>표에 세울 암종 — 현장에서 실제로 만난 것만, <b>위에서 아래 차례</b>로.</summary>
    public IReadOnlyList<RockClass> Rocks { get; }

    /// <summary>터파기를 깊이로 가르는가. 현장이 얕으면 <c>5m 초과</c> 줄이 통째로 빠진다.</summary>
    public IReadOnlyList<DepthClass> Depths { get; }

    /// <summary>육상만인가, 용수까지인가. 지하수위 자료가 없으면 <c>육상</c>뿐이다.</summary>
    public IReadOnlyList<WaterClass> Waters { get; }

    /// <summary>바닥면고르기를 적용할 암종 — <b>사용자가 정한다</b>(JACK).
    /// 실제 굴착 결과에 따라 보통암까지 넣기도, 경암을 빼기도 한다.</summary>
    public IReadOnlyList<RockClass> FloorTrim { get; }

    /// <summary>본문 줄 수 — <b>실행 중에 정해진다</b>. 축척이 이 값을 읽는다.</summary>
    public int BodyRows { get; }

    /// <summary>머리 한 줄을 더한 전체 줄 수.</summary>
    public int TotalRows => BodyRows + 1;

    /// <summary>왼쪽 줄 목록(수량 항목)과 오른쪽 줄 목록(공종). <b>길이가 같다</b> — 표는 직사각형이라야 한다.</summary>
    public IReadOnlyList<LeftRow> Left { get; }
    public IReadOnlyList<RightRow> Right { get; }

    /// <summary>왼쪽 한 줄 — 무엇의 수량인가.</summary>
    /// <param name="Group">대분류 글자(성토·절토·터파기·되메우기). 위 줄과 같으면 <c>null</c>(병합된다).</param>
    /// <param name="Sub">중분류 글자(터파기(육상) 등). 없으면 <c>null</c>.</param>
    /// <param name="Item">재료 글자(토 사·풍화암 …).</param>
    /// <param name="Key">이 줄이 읽어 갈 수량 열쇠. <c>null</c>이면 아직 못 구하는 항목이다.</param>
    public readonly record struct LeftRow(string Group, string Sub, string Item, QtyKey? Key);

    /// <summary>오른쪽 한 줄 — 공종. 값 열쇠는 아직 없다(길이 기반 수량은 다음 단계).</summary>
    public readonly record struct RightRow(string Item, string Sub);

    private QtyTableSpec(IReadOnlyList<RockClass> rocks, IReadOnlyList<DepthClass> depths,
                         IReadOnlyList<WaterClass> waters, IReadOnlyList<RockClass> floorTrim,
                         List<LeftRow> left, List<RightRow> right)
    {
        Rocks = rocks; Depths = depths; Waters = waters; FloorTrim = floorTrim;
        Left = left; Right = right; BodyRows = left.Count;
    }

    /// <summary>사람이 읽는 암종 이름 — 표에 그대로 적힌다(두 글자는 사이를 벌려 자리를 맞춘다).</summary>
    public static string NameOf(RockClass r) => r switch
    {
        RockClass.Soil => "토  사",
        RockClass.Weathered => "풍화암",
        RockClass.Soft => "연  암",
        RockClass.Medium => "보통암",
        RockClass.Hard => "경  암",
        _ => "?",
    };

    /// <summary>★★[JACK 0901 "암 이름에 띄어쓰기한 것들 다 붙여 — 표에서 헷갈리니깐"]
    /// <b>화면용 붙여 쓴 이름</b> — <c>연  암</c> → <c>연암</c>.
    /// <para>도면 토적표는 <see cref="NameOf"/>(벌려 쓴 것)를 그대로 쓴다 —
    /// 두 글자를 벌려 세 글자와 폭을 맞추는 것이 도면 관례이기 때문이다.
    /// 화면 표는 칸이 좁아 그 여백이 오히려 <b>글자 사이가 벌어진 것</b>처럼 보인다.</para>
    /// <para>★<b>이름을 두 벌로 적지 않는다</b> — 여기서 <see cref="NameOf"/>의 공백만 뗀다.
    /// 그래야 이름을 고칠 때 한 곳만 고쳐도 둘 다 따라온다(§50).</para></summary>
    public static string TightNameOf(RockClass r) => NameOf(r).Replace(" ", "");

    /// <summary>깊이 딱지 — <b>글자를 못 박지 않는다</b>. 기준 깊이가 바뀌면 따라 바뀐다.</summary>
    public static string DepthLabel(DepthClass d, double limitM)
        => d == DepthClass.Le ? $"{limitM:0.#}m|이하" : $"{limitM:0.#}m|초과";

    /// <summary>물 딱지.</summary>
    public static string WaterLabel(WaterClass w) => w == WaterClass.Land ? "육상" : "용수";


    // ── 셀 합치기 ★★★[JACK 0831 "셀 합치기가 이상하게 됐어"] ─────────────────────────
    //
    //   <b>이 셈은 여기 있어야 한다.</b> 처음엔 도면 그리는 쪽에 두었는데,
    //   <c>터파기 (용수)</c>가 <b>되메우기 줄까지 먹는</b> 잘못을 하니스가 못 잡았다 —
    //   도면 코드는 AutoCAD 없이 못 돌리기 때문이다.
    //   얼개가 곧 표 모양이므로 <b>몇 줄을 먹느냐도 얼개가 안다</b>. 그러면 검사가 걸린다.

    /// <summary>이 줄이 <b>두 단 길이를 맞추려 채운 빈 줄</b>인가 — 병합은 여기서 끊어야 한다.</summary>
    public bool IsFillerLeft(int r) =>
        r >= 0 && r < Left.Count
        && Left[r].Group == null && Left[r].Sub == null && Left[r].Item == null && Left[r].Key == null;

    public bool IsFillerRight(int r) =>
        r >= 0 && r < Right.Count && Right[r].Item == null && Right[r].Sub == null;

    /// <summary>대분류 칸이 <b>몇 줄</b>을 먹느냐. 0이면 위 칸이 이미 먹은 자리다.</summary>
    /// <param name="end">이 줄 <b>앞까지만</b> 먹는다(단 경계). 안 주면 표 끝까지.</param>
    public int SpanGroup(int r, int end = -1)
    {
        int lim = end < 0 ? BodyRows : System.Math.Min(end, BodyRows);
        if (r < 0 || r >= lim || Left[r].Group == null) return 0;
        int n = 1;
        for (int k = r + 1; k < lim; k++)
        {
            if (IsFillerLeft(k) || Left[k].Group != null) break;
            n++;
        }
        return n;
    }

    /// <summary>중분류 칸이 몇 줄을 먹느냐.
    /// <para>★<b>새 대분류가 시작하면 거기서 끝난다.</b> 이 조건이 빠져서
    /// <c>터파기 (용수)</c>가 <c>되메우기</c> 줄까지 먹었다(JACK 스샷) —
    /// 되메우기 줄은 중분류가 비어 있지만(대분류가 두 칸을 먹으므로) <b>남의 구역</b>이다.</para></summary>
    public int SpanSub(int r, int end = -1)
    {
        int lim = end < 0 ? BodyRows : System.Math.Min(end, BodyRows);
        if (r < 0 || r >= lim || Left[r].Sub == null) return 0;
        int n = 1;
        for (int k = r + 1; k < lim; k++)
        {
            if (IsFillerLeft(k) || Left[k].Sub != null || Left[k].Group != null) break;
            n++;
        }
        return n;
    }

    /// <summary>오른쪽 공종 칸이 몇 줄을 먹느냐.</summary>
    public int SpanRight(int r, int end = -1)
    {
        int lim = end < 0 ? BodyRows : System.Math.Min(end, BodyRows);
        if (r < 0 || r >= lim || Right[r].Item == null) return 0;
        int n = 1;
        for (int k = r + 1; k < lim; k++)
        {
            if (IsFillerRight(k) || Right[k].Item != null) break;
            n++;
        }
        return n;
    }

    /// <summary>대분류가 <b>중분류 칸까지</b> 먹는가 — 중분류가 없으면 두 칸이다(성토·절토·되메우기).</summary>
    public bool GroupTakesTwo(int r) => r >= 0 && r < BodyRows && Left[r].Group != null && Left[r].Sub == null;

    /// <summary>오른쪽 공종이 세부 칸까지 먹는가 — 세부가 없으면 두 칸이다(층 따 기·잡 석 부 설).</summary>
    public bool RightTakesTwo(int r) => r >= 0 && r < BodyRows && Right[r].Item != null && Right[r].Sub == null;

    /// <summary>★[검토 0907 · L-3] <b>한 단 안의 칸 자리에 이름을 준다.</b>
    /// <para>종전엔 <c>c0+1</c>·<c>c0+2</c>·<c>c0+3</c>이 그리는 쪽·검사·시험대 <b>세 곳에</b>
    /// 손으로 적혀 있었다. 단 모양을 바꿀 때 한 곳만 고쳐지면 글자가 엉뚱한 칸에 앉는다.</para>
    /// <para>공종 줄에서는 <see cref="ColItem"/>이 <b>세부</b> 자리로 쓰인다 — 재료와 세부가
    /// 같은 칸을 나눠 쓰는 것이 두 단 얼개의 뼈대다(§72).</para></summary>
    public const int ColGroup = 0, ColSub = 1, ColItem = 2, ColValue = 3;

    /// <summary>★★★[JACK 0907 "2칸 카테고리로"] <b>한 단은 넉 칸이다</b> — 대분류·중분류·재료·값.
    /// <para>왼쪽 대분류가 몇 칸을 먹느냐. 중분류가 없으면 두 칸(성토·절토·되메우기).</para></summary>
    public int LeftColSpan(int r) => GroupTakesTwo(r) ? 2 : 1;

    /// <summary>★★★[JACK 0907] <b>오른쪽 공종이 넉 칸 단에서 몇 칸을 먹느냐.</b>
    /// <para>공종에는 중분류가 없으므로 <b>늘 두 칸</b>(대분류+중분류 자리)을 먹고,
    /// 세부까지 없으면 <b>세 칸</b>이다(층 따 기·잡 석 부 설).</para>
    /// <para>★<b>이 셈이 한 벌뿐이라야 한다.</b> 종전엔 그리는 쪽과 검사하는 쪽이
    /// 각자 <c>RightTakesTwo(r) ? 2 : 1</c>을 적어 두었다 — 단 모양이 바뀌면
    /// 한쪽만 고쳐져 <b>검사가 그리지 않는 표를 재게 된다</b>(§53에서 겪었다).</para></summary>
    /// <param name="end">병합이 이 줄 <b>앞까지만</b> 미친다(단 경계).</param>
    public int RightColSpan(int r, int end = -1)
    {
        if (!RightTakesTwo(r)) return 2;
        // ★★★[검토 0907 · M-4] <b>세 칸 병합이 아랫줄의 세부를 삼키면 안 된다.</b>
        //   세 칸(<c>c0..c0+2</c>)은 세부 자리(<c>c0+2</c>)를 덮는다. 지금 자료로는
        //   세부 없는 공종(<c>층 따 기</c>·<c>잡 석 부 설</c>)이 <b>한 줄짜리</b>라 안 터지지만,
        //   나중에 <c>새공종 / (빈칸)+성토부</c> 꼴이 하나만 들어와도 그리는 쪽이
        //   <b>병합된 칸에 덮어써</b> 공종 이름이 세부로 바뀐다.
        //   → 병합이 미치는 줄 중 <b>하나라도</b> 세부가 있으면 두 칸으로 물러선다.
        int n = SpanRight(r, end);
        for (int k = r; k < r + n && k < Right.Count; k++)
            if (Right[k].Sub != null) return 2;
        return 3;
    }

    /// <summary>★★[JACK 0831] <b>병합이 서로 겹치지 않는가</b> — 검사가 이걸 물어야 한다.
    /// <para>겹치면 AutoCAD가 뒤 병합을 조용히 버리고 표가 찌그러진다.
    /// 칸마다 "누가 먹었나"를 칠해 보고 두 번 칠해지는 자리가 있으면 불합격이다.</para></summary>
    /// <summary>★[JACK 0831] <b>접은 표</b>를 검사한다 — 단마다 자기 구간만 칠한다.</summary>
    public bool MergesValid(QtyTableFold fold, out string why)
    {
        int rows = fold.BodyRows, cols = fold.Cols;
        var owner = new int[rows, cols];
        string bad = null;
        bool Paint(int r0, int c0, int rs, int cs, string what)
        {
            for (int r = r0; r < r0 + rs; r++)
                for (int c = c0; c < c0 + cs; c++)
                {
                    if (r >= rows || c >= cols) { bad = $"{what}({r0}줄 {c0}칸)이 표 밖으로 나간다"; return false; }
                    if (owner[r, c] != 0)
                    { bad = $"{what}({r0}줄 {c0}칸)이 겹친다 — {r}줄 {c}칸"; return false; }
                    owner[r, c] = r0 + 1;
                }
            return true;
        }
        foreach (var seg in fold.Segs)
        {
            int end = seg.From + seg.Count;
            for (int i = 0; i < seg.Count; i++)
            {
                int src = seg.From + i;
                // ★★★[JACK 0907] <b>한 단 안에 수량 항목과 공종이 <u>이어서</u> 들어온다.</b>
                //   종전엔 단 하나가 한 목록만 담아 <c>i</c>가 곧 줄 번호였다.
                //   이제는 <c>seg.Row</c>가 그 조각이 <b>단의 몇째 줄부터</b> 시작하는지 말해 준다.
                int row = seg.Row + i;
                if (seg.Left)
                {
                    int gs = SpanGroup(src, end);
                    if (gs > 0 && !Paint(row, seg.Col, gs, LeftColSpan(src), "대분류")) { why = bad; return false; }
                    int ss = SpanSub(src, end);
                    if (ss > 0 && !Paint(row, seg.Col + 1, ss, 1, "중분류")) { why = bad; return false; }
                }
                else
                {
                    int rs = SpanRight(src, end);
                    if (rs > 0 && !Paint(row, seg.Col, rs, RightColSpan(src, end), "공종")) { why = bad; return false; }
                }

                // ★★★[검토 0907 · M-4] <b>글자가 들어가는 칸도 칠한다.</b>
                //   종전엔 <b>병합만</b> 칠했다 — 그래서 "병합이 글자 칸을 삼켰다"를 못 잡았다.
                //   AutoCAD는 병합된 칸에 덮어써도 <b>말없이</b> 앞 글자를 지운다.
                //   그리는 쪽(<c>DrawQtyTables</c>)이 어느 칸에 쓰는지 <b>그대로</b> 옮겨 적는다 —
                //   이 검사가 곧 그림이라야 뜻이 있다(§53).
                if (seg.Left)
                {
                    if (Left[src].Item != null && !Paint(row, seg.Col + 2, 1, 1, "재료")) { why = bad; return false; }
                    if (!IsFillerLeft(src) && !Paint(row, seg.Col + 3, 1, 1, "값")) { why = bad; return false; }
                }
                else
                {
                    if (Right[src].Sub != null && !Paint(row, seg.Col + 2, 1, 1, "세부")) { why = bad; return false; }
                    if (!IsFillerRight(src) && !Paint(row, seg.Col + 3, 1, 1, "값")) { why = bad; return false; }
                }
            }
        }
        why = "";
        return true;
    }

    // ★★★[검토 0907 · M-2] <b>옛 7칸 판 검사를 지웠다.</b>
    //   접지 않은 7칸 얼개는 이제 <b>아무도 그리지 않는다</b>(단은 늘 둘, 8칸).
    //   그런데 그 검사가 시험대 세 곳에서 통과 도장을 찍고 있었고,
    //   <c>RightTakesTwo(r) ? 2 : 1</c>을 <b>제 손으로 다시 적어</b> 두 번째 벌이 됐다 —
    //   칸 셈을 한 곳으로 모은 이번 작업의 취지가 바로 거기서 새고 있었다(§53·§57·§72).

    /// <summary>★★★[JACK 0831] <b>실제로 값이 나온 조합만</b> 줄을 세운다 — 이것이 정본이다.
    ///
    /// <para>JACK: <i>"지층을 파악해서 합집합(한 단면이라도 지층이 포함된 게 있다면 모든 토적표에 포함)
    /// 형태로 최적화해서 표를 만들고"</i> · 인터뷰에서 <b>절토는 실제 깎인 암종만</b>,
    /// <b>터파기는 실제 나온 (암종·깊이·물) 조합만</b>으로 확정.</para>
    ///
    /// <para><b>합집합인 이유.</b> 표 모양은 도면 전체에서 <b>하나</b>라야 한다 —
    /// 측점마다 줄 수가 다르면 횡단면도마다 축척이 제각각이 되어 도면을 못 쓴다.
    /// 그래서 <b>어느 한 측점에서라도</b> 나온 조합은 모든 측점의 표에 자리를 갖고,
    /// 그 측점에 없으면 <c>–</c>로 남는다.</para>
    ///
    /// <para><b>차례는 고정</b>이다(무른 것→단단한 것, 육상→용수, 이하→초과) —
    /// 어느 측점에서 먼저 나왔느냐로 줄 차례가 달라지면 도면끼리 견줄 수 없다.</para>
    ///
    /// <para><see cref="Build"/>는 이제 <b>전부 곱한 열쇠 묶음</b>을 만들어 이 함수에 넘긴다 —
    /// 줄을 짓는 코드가 <b>한 벌</b>이라야 한쪽만 고쳐지는 일이 없다(§50).</para></summary>
    public static QtyTableSpec BuildFromKeys(IEnumerable<QtyKey> present,
                                             IReadOnlyList<RockClass> floorTrim = null,
                                             double limitM = 5.0)
    {
        var seen = new HashSet<QtyKey>();
        if (present != null) foreach (var k in present) seen.Add(k);

        // 나온 것만, 그러나 <b>정해진 차례로</b> 추린다.
        var cutRocks = new List<RockClass>();
        foreach (var r in RockOrder)
            if (seen.Contains(QtyKey.OfCut(r))) cutRocks.Add(r);

        var excRocks = new List<RockClass>();          // 터파기에 한 번이라도 나온 암종(바닥면고르기 기본값)
        var waters = new List<WaterClass>();
        var depths = new List<DepthClass>();
        foreach (var w in WaterOrder)
            foreach (var d in DepthOrder)
                foreach (var r in RockOrder)
                    if (seen.Contains(QtyKey.OfExc(r, d, w)))
                    {
                        if (!waters.Contains(w)) waters.Add(w);
                        if (!depths.Contains(d)) depths.Add(d);
                        if (!excRocks.Contains(r)) excRocks.Add(r);
                    }

        var rk = cutRocks.Count > 0 ? cutRocks : excRocks;   // 로그·견주기용 대표 암종 목록
        if (rk.Count == 0) rk = new List<RockClass> { RockClass.Soil };
        var trim = floorTrim ?? DefaultFloorTrim(excRocks);

        // ── 왼쪽: 수량 항목 ────────────────────────────────────────────────
        var L = new List<LeftRow>();

        // 성토는 언제나 토사 한 줄이다 — 쌓는 흙은 암종을 안 가린다.
        L.Add(new LeftRow("성    토", null, NameOf(RockClass.Soil), QtyKey.OfFill()));

        // 절토 — <b>실제 깎인</b> 암종만.
        for (int i = 0; i < cutRocks.Count; i++)
            L.Add(new LeftRow(i == 0 ? "절    토" : null, null, NameOf(cutRocks[i]), QtyKey.OfCut(cutRocks[i])));

        // 터파기 — <b>실제 나온</b> (깊이·물·암종)만. 대분류=깊이, 중분류=터파기(물).
        //
        // ★★★[JACK 0831 · 검토 HIGH-2] <b>깊이가 바깥, 물이 안쪽이다.</b>
        //   처음엔 물을 바깥에 뒀더니 대분류가 <c>5m이하 / 5m초과 / 5m이하 / 5m초과</c>로
        //   <b>네 덩어리로 쪼개졌다</b>. 한국 토적표 관례는 <b>"5.0m 이하"가 육상·용수를 통째로</b>
        //   먹는 모양이고(이 저장소가 베낀 원본 표가 그렇다), 읽는 사람도 깊이로 먼저 나눠 본다.
        //   → 깊이를 바깥에 두면 대분류가 두 덩어리로 합쳐진다.
        foreach (var d in DepthOrder)
        {
            if (!depths.Contains(d)) continue;
            bool firstOfDepth = true;                    // 대분류(깊이)는 <b>깊이 블록마다</b> 한 번
            foreach (var w in WaterOrder)
            {
                if (!waters.Contains(w)) continue;
                bool firstOfWater = true;                // 중분류(물)는 <b>물 블록마다</b> 한 번
                foreach (var r in RockOrder)
                {
                    var key = QtyKey.OfExc(r, d, w);
                    if (!seen.Contains(key)) continue;
                    L.Add(new LeftRow(
                        firstOfDepth ? DepthLabel(d, limitM) : null,
                        firstOfWater ? $"터파기|({WaterLabel(w)})" : null,
                        NameOf(r), key));
                    firstOfDepth = false; firstOfWater = false;
                }
            }
        }

        // 되메우기 — 구조물과 주위.
        L.Add(new LeftRow("되메우기", null, "구조물", null));
        L.Add(new LeftRow(null, null, "주  위", QtyKey.OfBackfill()));

        // ── 오른쪽: 공종 ──────────────────────────────────────────────────
        var R = new List<RightRow>
        {
            new("벌개재근", "성토부"), new(null, "절토부"),
            new("표토제거", "성토부"), new(null, "절토부"),
            new("면고르기", "성토부"), new(null, "절토부"),
        };
        // 바닥면고르기는 <b>터파기에 나온 암종</b>만큼 줄이 선다(JACK).
        for (int i = 0; i < trim.Count; i++)
            R.Add(new RightRow(i == 0 ? "바닥면고르기" : null, NameOf(trim[i])));
        R.Add(new RightRow("식생공법", "성토부"));
        R.Add(new RightRow(null, "절토부"));
        R.Add(new RightRow("층 따 기", null));
        R.Add(new RightRow("잡 석 부 설", null));

        // ── ★ 두 단의 길이를 맞춘다 — <b>표는 직사각형이라야 한다</b>.
        //   짧은 쪽에 <b>빈 셀</b>을 채운다(JACK 승인: <i>"공백 부분 셀로 표의 우측 부분을 마무리해도 돼"</i>).
        while (R.Count < L.Count) R.Add(new RightRow(null, null));
        while (L.Count < R.Count) L.Add(new LeftRow(null, null, null, null));

        if (waters.Count == 0) waters.Add(WaterClass.Land);
        if (depths.Count == 0) depths.Add(DepthClass.Le);
        return new QtyTableSpec(rk, depths, waters, trim, L, R);
    }

    /// <summary>암종·물·깊이의 <b>표준 차례</b> — 어디서든 이 차례로만 돈다.</summary>
    internal static readonly RockClass[] RockOrder =
        { RockClass.Soil, RockClass.Weathered, RockClass.Soft, RockClass.Medium, RockClass.Hard };
    internal static readonly WaterClass[] WaterOrder = { WaterClass.Land, WaterClass.Water };
    internal static readonly DepthClass[] DepthOrder = { DepthClass.Le, DepthClass.Gt };

    /// <summary>★ 표를 짓는다.
    /// <param name="rocks">현장에서 만난 암종. 비어 있으면 <c>토사</c> 하나로 본다.</param>
    /// <param name="hasDeep">터파기가 기준 깊이를 넘는 데가 있는가 — 없으면 초과 줄을 안 만든다.</param>
    /// <param name="hasWater">지하수위 아래를 파는 데가 있는가 — 없으면 용수 줄을 안 만든다.</param>
    /// <param name="floorTrim">바닥면고르기를 적용할 암종(사용자 설정). <c>null</c>이면 토사를 뺀 전부.</param>
    /// <param name="limitM">깊이를 가르는 기준(m). 표 글자와 계산이 <b>같은 값</b>을 써야 한다.</param></summary>
    public static QtyTableSpec Build(IReadOnlyList<RockClass> rocks, bool hasDeep, bool hasWater,
                                     IReadOnlyList<RockClass> floorTrim = null, double limitM = 5.0)
    {
        // ★★[JACK 0831] <b>줄을 짓는 코드는 한 벌뿐이다.</b>
        //   여기서는 <b>전부 곱한 열쇠 묶음</b>만 만들어 <see cref="BuildFromKeys"/>에 넘긴다 —
        //   종전엔 줄 짓는 코드가 여기 통째로 있었고, 실제 나온 것만 세우는 길을 새로 만들면서
        //   <b>같은 코드가 두 벌</b>이 될 뻔했다. 두 벌이면 언젠가 한쪽만 고쳐진다(§50).
        var rk = Normalize(rocks);
        var keys = new List<QtyKey> { QtyKey.OfFill(), QtyKey.OfBackfill() };
        foreach (var r in rk) keys.Add(QtyKey.OfCut(r));
        foreach (var w in hasWater ? WaterOrder : new[] { WaterClass.Land })
            foreach (var d in hasDeep ? DepthOrder : new[] { DepthClass.Le })
                foreach (var r in rk) keys.Add(QtyKey.OfExc(r, d, w));
        return BuildFromKeys(keys, floorTrim ?? DefaultFloorTrim(rk), limitM);
    }

    /// <summary>암종 목록을 <b>표준 차례</b>(무른 것 → 단단한 것)로 정리하고 중복을 없앤다.
    /// <para>사용자가 도킹바에서 아무 순서로 골라도 표는 늘 같은 차례로 선다 —
    /// 도면끼리 견줄 수 있어야 하기 때문이다.</para></summary>
    private static List<RockClass> Normalize(IReadOnlyList<RockClass> src)
    {
        var seen = new bool[5];
        if (src != null) foreach (var r in src) seen[(int)r] = true;
        var r2 = new List<RockClass>();
        foreach (RockClass r in new[] { RockClass.Soil, RockClass.Weathered, RockClass.Soft,
                                        RockClass.Medium, RockClass.Hard })
            if (seen[(int)r]) r2.Add(r);
        if (r2.Count == 0) r2.Add(RockClass.Soil);   // 아무것도 없으면 토사 하나 — 빈 표를 만들지 않는다
        return r2;
    }

    /// <summary>바닥면고르기 기본값 — <b>토사를 뺀</b> 암종. 흙바닥은 고르기 대상이 아니다.</summary>
    private static List<RockClass> DefaultFloorTrim(IReadOnlyList<RockClass> rocks)
    {
        var r = new List<RockClass>();
        foreach (var x in rocks) if (x != RockClass.Soil) r.Add(x);
        return r;
    }
}

/// <summary>★★★[JACK 0831 "표를 좀 어떻게 하면 모든 상황에 대처해서 최대한 빈 셀이 없게 쓸 수 있지?"
/// · JACK 0907 "2칸 카테고리로 나와야 해 · 마지막 카테고리에만 공백이 생겨야 해"]
/// <b>표를 두 단으로 세우고, 내용을 <u>이어서</u> 흘린다.</b>
///
/// <para><b>종전은 무엇이 문제였나.</b> 왼쪽(수량 항목)과 오른쪽(공종)을 <b>따로</b> 세워 놓고
/// 긴 쪽만 접었다. 그러면 단이 <b>셋</b>이 되고(JACK 스샷: 성토·벌개재근·면고르기가 한 줄),
/// 짧은 단마다 <b>제 아래에 빈칸</b>이 생겼다 — 1단 밑에도 2단 밑에도 구멍이 났다.</para>
///
/// <para><b>새 규칙.</b> 수량 항목과 공종을 <b>한 줄기</b>로 잇는다(항목이 먼저, 공종이 뒤).
/// 그 줄기를 <b>두 단</b>에만 나눠 담되, <b>1단을 꽉 채우고</b> 남는 것을 2단에 붓는다.
/// 그래서 빈칸은 <b>마지막 단 아래</b>에만 생긴다.</para>
///
/// <code>
/// ┌───────────────────────┬───────────────────────┐
/// │ 성    토 │토 사│ –    │ 면고르기 │성토부│ –   │
/// │ 절    토 │토 사│ –    │          │절토부│ –   │
/// │ 되메우기 │구조물│ –   │ 식생공법 │성토부│ –   │
/// │          │주  위│ –   │          │절토부│ –   │
/// │ 벌개재근 │성토부│ –   │ 층  따  기      │ –   │
/// │          │절토부│ –   │ 잡 석 부 설     │ –   │
/// │ 표토제거 │성토부│ –   │                 │     │← 빈칸은 여기만
/// │          │절토부│ –   │                 │     │
/// └───────────────────────┴───────────────────────┘
/// </code>
///
/// <para><b>끊는 자리는 블록 경계다.</b> 줄 수로 반 나누면 <c>터파기 (용수)</c> 한가운데가 잘려
/// 대분류·중분류 병합이 두 단에 걸친다 — 읽기도 나쁘고 병합도 못 한다.
/// 그래서 <b>대분류(또는 공종)가 새로 시작하는 자리</b>에서만 끊는다.</para>
///
/// <para><b>한 단은 넉 칸이다</b> — 대분류·중분류·재료·값. 공종 줄은 중분류가 없으므로
/// 앞 두 칸을 합쳐 쓴다. 좌우 짝 폭이 이미 같게 맞춰져 있어(<c>A+B=E · C=F · D=G</c>)
/// 두 단의 세로선이 <b>저절로</b> 나란해진다 — 그 규칙이 여기서 비로소 쓸모를 낸다.</para></summary>
public sealed class QtyTableFold
{
    /// <summary>단 하나의 조각 — 어느 목록(<paramref name="Left"/>)의 <paramref name="From"/>부터
    /// <paramref name="Count"/>줄을, <paramref name="Col"/> 칸 <paramref name="Row"/>줄부터 그린다.</summary>
    /// <param name="Row">★[JACK 0907] <b>단 안에서 몇째 줄부터인가</b>(0이 첫 줄).
    /// 한 단에 수량 항목과 공종이 <b>잇달아</b> 들어오므로 이 값이 있어야 한다.</param>
    public readonly record struct Seg(bool Left, int From, int Count, int Col, int Row);

    /// <summary>본문 줄 수(머리줄 제외).</summary>
    public int BodyRows { get; }

    /// <summary>전체 칸 수 — 두 단이면 8, 끊을 자리가 없어 한 단이면 4.</summary>
    public int Cols { get; }

    public IReadOnlyList<Seg> Segs { get; }

    /// <summary>각 칸이 <b>원래 어느 칸의 폭</b>을 쓰나 — <c>QuantityTable.ColRatio</c>의 순번.</summary>
    public IReadOnlyList<int> ColRatioIndex { get; }

    /// <summary>왜 이렇게 나눴나 — 로그에 그대로 쓴다.</summary>
    public string Note { get; }

    /// <summary>★[JACK 0907] <b>빈칸이 마지막 단에만 있는가.</b> 그리는 쪽이 로그에 남긴다 —
    /// 블록이 너무 커서 못 지킨 판이 있으면 <b>말없이 넘어가지 않는다</b>.</summary>
    public bool TailOnlyGap { get; }

    /// <summary>★★★[JACK 0907 "마지막 공백칸은 모두 셀병합하고 대각선 선 하나만 넣어서 마무리해줘"]
    /// <b>한 단에서 찬 줄과 빈 줄.</b>
    /// <para>도면 관례다: 표 끝의 남는 칸은 <b>하나로 합치고 대각선</b>을 그어
    /// "여기는 더 없다"를 눈으로 말한다. 빈칸을 그냥 두면 <b>아직 안 적은 것</b>으로 읽힌다 —
    /// 이 저장소가 <c>–</c>와 빈칸을 갈라 쓰는 이유와 같다.</para>
    /// <para>★<b>단마다</b> 들고 있다. 원칙(두 단)에서는 마지막 단에만 빈칸이 있지만,
    /// 표가 커져 세 단으로 물러선 판에서는 앞 단에도 생길 수 있다 — <b>그 자리도 같이 마무리한다</b>.</para></summary>
    /// <param name="Col">그 단이 시작하는 칸 번호.</param>
    /// <param name="Filled">그 단에 실제로 든 줄 수 — 빈칸은 <b>이 줄 다음부터</b>다.</param>
    /// <param name="Blank">비어 있는 줄 수. <c>0</c>이면 딱 맞아 합칠 자리가 없다.</param>
    public readonly record struct PanelGap(int Col, int Filled, int Blank);

    /// <summary>단마다의 빈칸 — 그리는 쪽이 <b>합치고 대각선</b>을 그을 자리다.</summary>
    public IReadOnlyList<PanelGap> Gaps { get; }

    private QtyTableFold(int bodyRows, int cols, IReadOnlyList<Seg> segs,
                         IReadOnlyList<int> ratioIx, string note, bool tailOnly,
                         IReadOnlyList<PanelGap> gaps)
    {
        BodyRows = bodyRows; Cols = cols; Segs = segs; ColRatioIndex = ratioIx;
        Note = note; TailOnlyGap = tailOnly; Gaps = gaps;
    }

    /// <summary>한 단(4칸)이 쓰는 폭 순번 — 대분류·중분류·재료·값.</summary>
    private static readonly int[] PanelCols = { 0, 1, 2, 3 };

    /// <summary>★[JACK 0907] <b>원칙은 두 단</b>이다 — 셋이 되면 한 줄에 카테고리가 셋 나온다.
    /// <para>표가 커서 고른 배치의 칸에 <b>물리적으로 안 들어갈 때만</b> 그리는 쪽이 셋을 청한다
    /// (JACK 0907: <i>"그 도면만 표를 3칸으로"</i>). 배치도 장 수도 그대로 둔다.</para></summary>
    public const int Panels = 2;

    /// <summary>한 단이 몇 칸인가.</summary>
    public const int PanelWidth = 4;

    /// <summary>★ 표를 <paramref name="panels"/>개 단으로 나눈다.
    ///
    /// <para><b>원칙은 두 단</b>이다(JACK 0907). 그런데 지층이 다 나오는 현장은 표가 26줄까지 커져
    /// <b>2×3처럼 칸이 낮은 배치에서는 물리적으로 안 들어간다</b> — 표 203mm에 칸 자리 135mm다.
    /// 그때만 <b>그 도면의 표를</b> 세 단으로 나눈다(JACK 0907: <i>"그 도면만 표를 3칸으로"</i>).
    /// 배치는 사용자가 고른 대로 두고, 장 수도 그대로다.</para>
    ///
    /// <para><b>고르는 법은 단이 몇이든 같다.</b> 앞 단들이 <b>정확히 꽉 차야</b>(빈칸 0)
    /// 빈칸이 마지막 단에만 남는다 — 즉 <c>1×rows</c>·<c>2×rows</c>…가 전부 <b>블록 경계</b>라야 한다.
    /// 그런 <c>rows</c> 중 가장 작은 것을 고른다.</para>
    ///
    /// <para>그런 <c>rows</c>가 없으면(블록 크기가 안 맞아떨어지면) <b>욕심껏 담기</b>로 물러선다 —
    /// 앞 단에도 빈칸이 조금 생기지만 표는 선다. 물러섰다는 것을 <see cref="TailOnlyGap"/>과
    /// <see cref="Note"/>가 <b>말한다</b>.</para></summary>
    public static QtyTableFold Make(QtyTableSpec spec, int panels = Panels)
    {
        int L = ContentRows(spec, true), R = ContentRows(spec, false);
        int N = L + R;
        var segs = new List<Seg>();

        if (spec == null || N <= 0)
            return new QtyTableFold(1, PanelWidth, segs, new List<int>(PanelCols), "표가 비었다", true,
                                    new List<PanelGap>());
        if (panels < 1) panels = 1;

        // ── 블록 — <b>끊을 수 있는 자리</b>로 잘린 덩어리. 이 안은 절대 안 자른다.
        //   (왼쪽은 대분류가 새로 서는 줄, 오른쪽은 공종이 새로 서는 줄, 그리고 항목→공종 경계.)
        var bound = new List<int> { 0 };
        for (int r = 1; r < L; r++) if (spec.Left[r].Group != null) bound.Add(r);
        if (L > 0 && R > 0) bound.Add(L);
        for (int r = 1; r < R; r++) if (spec.Right[r].Item != null) bound.Add(L + r);
        bound.Add(N);                                   // 끝도 경계다
        var isBound = new HashSet<int>(bound);

        // ── 앞 단들이 <b>정확히</b> 꽉 차는 가장 작은 줄 수.
        int maxBlock = 0;
        for (int i = 0; i + 1 < bound.Count; i++)
            maxBlock = System.Math.Max(maxBlock, bound[i + 1] - bound[i]);
        int lo = System.Math.Max(maxBlock, (N + panels - 1) / panels);

        int rows = -1;
        bool exact = false;
        for (int cand = lo; cand <= N; cand++)
        {
            bool ok = true;
            for (int p = 1; p < panels && ok; p++)
                if (!isBound.Contains(p * cand)) ok = false;      // 앞 단 끝이 블록 경계라야 한다
            int last = N - (panels - 1) * cand;
            if (ok && last > 0 && last <= cand) { rows = cand; exact = true; break; }
        }

        // ── 물러서기 — 욕심껏 담는다. 앞 단에도 빈칸이 조금 생긴다.
        var cuts = new List<int>();
        if (exact)
        {
            for (int p = 1; p < panels; p++) cuts.Add(p * rows);
        }
        else
        {
            for (int cand = lo; cand <= N && rows < 0; cand++)
            {
                var c2 = Greedy(bound, cand, panels);
                if (c2 != null) { rows = cand; cuts = c2; }
            }
            if (rows < 0) { rows = N; cuts.Clear(); }             // 한 단으로라도 세운다
        }

        // ── 자리에 앉힌다.
        int used = cuts.Count + 1;                                // 실제로 쓴 단 수
        var gaps = new List<PanelGap>();
        int at = 0;
        for (int p = 0; p < used; p++)
        {
            int end = p < cuts.Count ? cuts[p] : N;
            Emit(segs, L, at, end - at, p * PanelWidth);
            gaps.Add(new PanelGap(p * PanelWidth, end - at, rows - (end - at)));
            at = end;
        }

        var ix = new List<int>();
        for (int p = 0; p < used; p++) ix.AddRange(PanelCols);

        bool tailOnly = true;
        for (int p = 0; p + 1 < gaps.Count; p++) if (gaps[p].Blank != 0) tailOnly = false;

        var sb = new System.Text.StringBuilder($"{used}단 — ");
        for (int p = 0; p < gaps.Count; p++)
            sb.Append($"{p + 1}단 {gaps[p].Filled}줄(빈 {gaps[p].Blank})").Append(p + 1 < gaps.Count ? " · " : "");
        sb.Append($" · {rows}줄 {used * PanelWidth}칸");
        if (!exact) sb.Append(" ⚠딱 나뉘지 않아 욕심껏 담았다(앞 단에도 빈칸)");

        return new QtyTableFold(rows, used * PanelWidth, segs, ix, sb.ToString(), tailOnly, gaps);
    }

    /// <summary>욕심껏 담는다 — 각 단에 <b>들어갈 수 있는 만큼</b>. 못 담으면 <c>null</c>.</summary>
    private static List<int> Greedy(List<int> bound, int rows, int panels)
    {
        var cuts = new List<int>();
        int start = 0;
        for (int i = 1; i < bound.Count; i++)
        {
            if (bound[i] - start <= rows) continue;               // 아직 이 단에 들어간다
            if (bound[i - 1] == start) return null;               // 블록 하나가 단보다 크다
            cuts.Add(bound[i - 1]);
            start = bound[i - 1];
            if (cuts.Count > panels - 1) return null;
            if (bound[i] - start > rows) return null;             // 그래도 안 들어간다
        }
        return cuts.Count <= panels - 1 ? cuts : null;
    }

    /// <summary>★★★[검토 0907 · M-1] <b>두 단의 칸 폭이 나란한가</b> — 그리는 것을 재는 검사.
    /// <para>종전엔 <c>QuantityTable.WidthsPaired</c>(A+B=E · C=F · D=G)를 물어 로그에 찍었는데,
    /// 두 단이 된 뒤로 <c>ColRatio[4..6]</c>(E·F·G)은 <b>어디서도 안 쓰인다</b> —
    /// 맞는 도면에 "어긋남"이라 말하고, 틀린 도면에 "맞음"이라 말할 수 있었다(§53의 되풀이).</para>
    /// <para>지금 재는 것: 두 단이 <b>같은 폭 순번</b>을 쓰는가. 어긋나면 가운데 세로선이 안 맞는다.</para></summary>
    public bool PanelsAligned(out string note)
    {
        if (Cols == PanelWidth) { note = $"한 단({PanelWidth}칸)"; return true; }
        if (Cols != PanelWidth * Panels) { note = $"칸이 {Cols}개다(두 단이면 {PanelWidth * Panels}칸)"; return false; }
        for (int c = 0; c < PanelWidth; c++)
            if (ColRatioIndex[c] != ColRatioIndex[c + PanelWidth])
            {
                note = $"{c}칸 폭이 두 단에서 다르다(순번 {ColRatioIndex[c]} ↔ {ColRatioIndex[c + PanelWidth]})";
                return false;
            }
        note = $"두 단 폭이 같다(순번 {string.Join(",", ColRatioIndex)})";
        return true;
    }

    /// <summary>채움 줄을 뺀 <b>실제 내용</b> 줄 수.</summary>
    /// <summary>줄기의 <paramref name="from"/>부터 <paramref name="count"/>줄을 <paramref name="col"/> 단에 붓는다.
    /// <para>줄기는 <b>수량 항목 <paramref name="L"/>줄 + 공종</b>으로 이어져 있으므로,
    /// 한 단이 두 목록에 걸치면 조각이 <b>둘</b>로 나온다.</para></summary>
    private static void Emit(List<Seg> segs, int L, int from, int count, int col)
    {
        if (count <= 0) return;
        int end = from + count, row = 0;
        int lEnd = System.Math.Min(end, L);
        if (from < lEnd) { segs.Add(new Seg(true, from, lEnd - from, col, 0)); row = lEnd - from; }
        int rFrom = System.Math.Max(from, L) - L, rEnd = end - L;
        if (rEnd > rFrom) segs.Add(new Seg(false, rFrom, rEnd - rFrom, col, row));
    }

    private static int ContentRows(QtyTableSpec spec, bool left)
    {
        if (spec == null) return 0;
        int last = -1;
        for (int r = 0; r < spec.BodyRows; r++)
            if (!(left ? spec.IsFillerLeft(r) : spec.IsFillerRight(r))) last = r;
        return last + 1;
    }
}

/// <summary>수량 하나를 가리키는 열쇠. <b>표와 계산이 같은 열쇠를 쓴다</b> —
/// 종전엔 줄 번호로 이어 붙였는데, 줄이 실행 중에 늘고 줄면 번호가 곧 어긋난다.</summary>
public enum QtyKeyKind { Fill, Cut, Exc, Backfill }

public readonly record struct QtyKey(QtyKeyKind Kind, RockClass Rock, DepthClass Depth, WaterClass Water)
{
    public static QtyKey OfFill() => new(QtyKeyKind.Fill, RockClass.Soil, DepthClass.Le, WaterClass.Land);
    public static QtyKey OfCut(RockClass r) => new(QtyKeyKind.Cut, r, DepthClass.Le, WaterClass.Land);
    public static QtyKey OfExc(RockClass r, DepthClass d, WaterClass w) => new(QtyKeyKind.Exc, r, d, w);
    public static QtyKey OfBackfill() => new(QtyKeyKind.Backfill, RockClass.Soil, DepthClass.Le, WaterClass.Land);
}

/// <summary>한 측점의 수량 장부 — <b>열쇠로 넣고 열쇠로 뺀다</b>.
/// <para>없는 열쇠를 물으면 <c>NaN</c>이다 — <b>0이 아니다</b>. 이 저장소가 여러 번 데인 자리다:
/// 재지 않은 것과 재서 0인 것은 다르다.</para></summary>
public sealed class QtyLedger
{
    private readonly Dictionary<QtyKey, double> _v = new();

    /// <summary>더한다(같은 열쇠로 여러 번 넣으면 쌓인다).</summary>
    public void Add(QtyKey k, double v)
    {
        if (double.IsNaN(v)) return;                     // 못 잰 것은 안 담는다
        _v[k] = _v.TryGetValue(k, out double old) ? old + v : v;
    }

    /// <summary>꺼낸다. 없으면 <c>NaN</c>.</summary>
    public double Get(QtyKey k) => _v.TryGetValue(k, out double v) ? v : double.NaN;

    /// <summary>★[JACK 0831] 담긴 열쇠들 — <b>합집합</b>을 모을 때 쓴다.</summary>
    public IEnumerable<QtyKey> Keys => _v.Keys;

    /// <summary>담긴 자리 수 — 로그에 "몇 칸이 찼나"를 적을 때 쓴다.</summary>
    public int Count => _v.Count;
}

/// <summary>★★★[JACK 0828] <b>표가 지켜야 할 약속</b> — 하니스가 이 자를 쓴다.
///
/// <para>JACK: <i>"토적표에서 추가되거나 생략되는 건 절토·터파기·바닥면고르기 부분이야.
/// 이 부분들을 제외하고는 그냥 상시 표가 만들어져 있는 걸로 해."</i></para>
///
/// <para>그 약속을 <b>말이 아니라 검사</b>로 남긴다. 표를 짓는 코드는 앞으로도 고쳐질 텐데,
/// 고치는 사람이 이 규칙을 모르고 성토나 잡석부설을 조건부로 만들면 <b>여기서 걸린다</b>.
/// 이 저장소가 여러 번 겪은 것 — <b>검사는 돌아야 검사다</b>.</para></summary>
public static class QtyTableSpecRules
{
    /// <summary>현장이 무엇이든 <b>늘 서 있어야 하는</b> 왼쪽 대분류.</summary>
    public static readonly string[] AlwaysLeft = { "성    토", "되메우기" };

    /// <summary>현장이 무엇이든 <b>늘 서 있어야 하는</b> 오른쪽 공종.</summary>
    public static readonly string[] AlwaysRight =
        { "벌개재근", "표토제거", "면고르기", "식생공법", "층 따 기", "잡 석 부 설" };

    /// <summary>약속을 지켰는가. 어긋나면 <paramref name="why"/>에 <b>무엇이 빠졌는지</b> 적는다 —
    /// <b>참/거짓만 돌려주면 왜 깨졌는지 찾는 데 또 하루가 든다.</b></summary>
    public static bool Holds(QtyTableSpec spec, out string why)
    {
        why = "";
        if (spec == null) { why = "표가 없다"; return false; }
        var miss = new List<string>();

        foreach (string t in AlwaysLeft)
        {
            bool found = false;
            foreach (var r in spec.Left) if (r.Group == t) { found = true; break; }
            if (!found) miss.Add("왼쪽 '" + t + "'");
        }
        foreach (string t in AlwaysRight)
        {
            bool found = false;
            foreach (var r in spec.Right) if (r.Item == t) { found = true; break; }
            if (!found) miss.Add("오른쪽 '" + t + "'");
        }
        if (spec.Left.Count != spec.Right.Count)
            miss.Add($"두 단 길이가 다르다({spec.Left.Count}/{spec.Right.Count})");

        why = miss.Count == 0 ? "" : "빠졌다: " + string.Join(" · ", miss);
        return miss.Count == 0;
    }
}
