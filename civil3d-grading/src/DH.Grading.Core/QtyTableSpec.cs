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
                if (seg.Left)
                {
                    int gs = SpanGroup(src, end);
                    if (gs > 0 && !Paint(i, seg.Col, gs, GroupTakesTwo(src) ? 2 : 1, "대분류")) { why = bad; return false; }
                    int ss = SpanSub(src, end);
                    if (ss > 0 && !Paint(i, seg.Col + 1, ss, 1, "중분류")) { why = bad; return false; }
                }
                else
                {
                    int rs = SpanRight(src, end);
                    if (rs > 0 && !Paint(i, seg.Col, rs, RightTakesTwo(src) ? 2 : 1, "공종")) { why = bad; return false; }
                }
            }
        }
        why = "";
        return true;
    }

    public bool MergesValid(out string why)
    {
        int rows = BodyRows, cols = 7;
        var owner = new int[rows, cols];        // 0=빈칸, 그 외=주인 줄 번호+1
        string bad = null;
        bool Paint(int r0, int c0, int rs, int cs, string what)
        {
            for (int r = r0; r < r0 + rs; r++)
                for (int c = c0; c < c0 + cs; c++)
                {
                    if (r >= rows || c >= cols) { bad = $"{what}({r0}줄)이 표 밖으로 나간다"; return false; }
                    if (owner[r, c] != 0)
                    { bad = $"{what}({r0}줄)이 {owner[r, c] - 1}줄 것과 겹친다 — {r}줄 {c}칸"; return false; }
                    owner[r, c] = r0 + 1;
                }
            return true;
        }
        for (int r = 0; r < rows; r++)
        {
            int gs = SpanGroup(r);
            if (gs > 0 && !Paint(r, 0, gs, GroupTakesTwo(r) ? 2 : 1, "대분류")) { why = bad; return false; }
            int ss = SpanSub(r);
            if (ss > 0 && !Paint(r, 1, ss, 1, "중분류")) { why = bad; return false; }
            int rs = SpanRight(r);
            if (rs > 0 && !Paint(r, 4, rs, RightTakesTwo(r) ? 2 : 1, "공종")) { why = bad; return false; }
        }
        why = "";
        return true;
    }

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

/// <summary>★★★[JACK 0831 "표를 좀 어떻게 하면 모든 상황에 대처해서 최대한 빈 셀이 없게 쓸 수 있지?"]
/// <b>긴 쪽을 두 단으로 접는다.</b>
///
/// <para><b>왜 빈칸이 생기나.</b> 왼쪽(수량 항목)은 <b>지층</b>이 정하고 오른쪽(공종)은 거의 고정이다.
/// 왼쪽 4~28줄 · 오른쪽 10~14줄이라 어느 쪽이 길어질지도 현장마다 다르다 —
/// 표는 직사각형이라야 하므로 짧은 쪽에 <b>빈 줄</b>이 남는다.</para>
///
/// <para><b>접으면 두 가지가 같이 좋아진다.</b> 왼쪽 28 · 오른쪽 14면
/// 왼쪽을 14+14 두 단으로 나눠 <b>빈칸이 0</b>이 되고, <b>표 높이도 절반</b>이라 축척이 살아난다.</para>
///
/// <para><b>끊는 자리는 블록 경계다.</b> 줄 수로 반 나누면 <c>터파기 (용수)</c> 한가운데가 잘려
/// 대분류·중분류 병합이 두 단에 걸친다 — 읽기도 나쁘고 병합도 못 한다.
/// 그래서 <b>대분류가 새로 시작하는 자리</b>에서만 끊고, 그중 가장 고른 곳을 고른다.</para>
///
/// <para>경우의 수는 많지만 <b>세지 않는다</b> — 줄 수 두 개와 경계 목록으로 <b>산수</b>를 한다.</para></summary>
public sealed class QtyTableFold
{
    /// <summary>단 하나 — 어느 목록(<paramref name="Left"/>)의 <paramref name="From"/>부터
    /// <paramref name="Count"/>줄을 <paramref name="Col"/> 칸부터 그린다.</summary>
    public readonly record struct Seg(bool Left, int From, int Count, int Col);

    /// <summary>본문 줄 수(머리줄 제외).</summary>
    public int BodyRows { get; }

    /// <summary>전체 칸 수 — 안 접으면 7, 왼쪽을 접으면 11, 오른쪽을 접으면 10.</summary>
    public int Cols { get; }

    public IReadOnlyList<Seg> Segs { get; }

    /// <summary>각 칸이 <b>원래 어느 칸의 폭</b>을 쓰나 — <c>QuantityTable.ColRatio</c>의 순번.</summary>
    public IReadOnlyList<int> ColRatioIndex { get; }

    /// <summary>왜 이렇게 접었나 — 로그에 그대로 쓴다.</summary>
    public string Note { get; }

    private QtyTableFold(int bodyRows, int cols, IReadOnlyList<Seg> segs,
                         IReadOnlyList<int> ratioIx, string note)
    { BodyRows = bodyRows; Cols = cols; Segs = segs; ColRatioIndex = ratioIx; Note = note; }

    /// <summary>왼쪽 한 단(4칸) 폭 순번.</summary>
    private static readonly int[] LeftCols = { 0, 1, 2, 3 };
    /// <summary>오른쪽 한 단(3칸) 폭 순번.</summary>
    private static readonly int[] RightCols = { 4, 5, 6 };

    /// <summary>★ 접을지 말지 정한다.
    /// <param name="gapMax">빈 줄이 이만큼 이하면 <b>접지 않는다</b> — 두세 줄 때문에 표를 넓히는 것은 손해다.</param></summary>
    public static QtyTableFold Make(QtyTableSpec spec, int gapMax = 4)
    {
        int L = ContentRows(spec, true), R = ContentRows(spec, false);
        var segs = new List<Seg>();
        var ix = new List<int>();

        // ── 안 접는다 — 차이가 작으면 넓히는 손해가 더 크다.
        if (System.Math.Abs(L - R) <= gapMax)
        {
            int rows = System.Math.Max(System.Math.Max(L, R), 1);
            segs.Add(new Seg(true, 0, L, 0));
            segs.Add(new Seg(false, 0, R, 4));
            ix.AddRange(LeftCols); ix.AddRange(RightCols);
            return new QtyTableFold(rows, 7, segs, ix,
                $"안 접음(왼쪽 {L} · 오른쪽 {R} · 빈 {System.Math.Abs(L - R)}줄)");
        }

        // ── 긴 쪽을 두 단으로.
        bool foldLeft = L > R;
        int len = foldLeft ? L : R;
        int cut = BestCut(spec, foldLeft, len);
        int a = cut, b = len - cut;
        int rows2 = System.Math.Max(System.Math.Max(a, b), foldLeft ? R : L);
        if (rows2 < 1) rows2 = 1;

        if (foldLeft)
        {
            segs.Add(new Seg(true, 0, a, 0));
            segs.Add(new Seg(true, cut, b, 4));
            segs.Add(new Seg(false, 0, R, 8));
            ix.AddRange(LeftCols); ix.AddRange(LeftCols); ix.AddRange(RightCols);
            return new QtyTableFold(rows2, 11, segs, ix,
                $"왼쪽을 {a}+{b}로 접음(오른쪽 {R}) — {rows2}줄 · 빈 {rows2 * 2 - L + rows2 - R}칸분");
        }
        segs.Add(new Seg(true, 0, L, 0));
        segs.Add(new Seg(false, 0, a, 4));
        segs.Add(new Seg(false, cut, b, 7));
        ix.AddRange(LeftCols); ix.AddRange(RightCols); ix.AddRange(RightCols);
        return new QtyTableFold(rows2, 10, segs, ix,
            $"오른쪽을 {a}+{b}로 접음(왼쪽 {L}) — {rows2}줄");
    }

    /// <summary>채움 줄을 뺀 <b>실제 내용</b> 줄 수.</summary>
    private static int ContentRows(QtyTableSpec spec, bool left)
    {
        int last = -1;
        for (int r = 0; r < spec.BodyRows; r++)
            if (!(left ? spec.IsFillerLeft(r) : spec.IsFillerRight(r))) last = r;
        return last + 1;
    }

    /// <summary>가장 고르게 나뉘는 <b>블록 경계</b>를 고른다.
    /// <para>경계가 하나도 없으면(있을 수 없지만) 절반에서 끊는다 — 그래도 표는 서야 한다.</para></summary>
    private static int BestCut(QtyTableSpec spec, bool left, int len)
    {
        int best = -1, bestBad = int.MaxValue;
        for (int r = 1; r < len; r++)
        {
            bool boundary = left ? spec.Left[r].Group != null : spec.Right[r].Item != null;
            if (!boundary) continue;
            int bad = System.Math.Max(r, len - r);          // 두 단 중 <b>긴 쪽</b>이 표 높이를 정한다
            if (bad < bestBad) { bestBad = bad; best = r; }
        }
        return best > 0 ? best : (len + 1) / 2;
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
