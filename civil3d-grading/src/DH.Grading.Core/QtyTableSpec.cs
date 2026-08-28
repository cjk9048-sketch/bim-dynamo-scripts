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

    /// <summary>깊이 딱지 — <b>글자를 못 박지 않는다</b>. 기준 깊이가 바뀌면 따라 바뀐다.</summary>
    public static string DepthLabel(DepthClass d, double limitM)
        => d == DepthClass.Le ? $"{limitM:0.#}m|이하" : $"{limitM:0.#}m|초과";

    /// <summary>물 딱지.</summary>
    public static string WaterLabel(WaterClass w) => w == WaterClass.Land ? "육상" : "용수";

    /// <summary>★ 표를 짓는다.
    /// <param name="rocks">현장에서 만난 암종. 비어 있으면 <c>토사</c> 하나로 본다.</param>
    /// <param name="hasDeep">터파기가 기준 깊이를 넘는 데가 있는가 — 없으면 초과 줄을 안 만든다.</param>
    /// <param name="hasWater">지하수위 아래를 파는 데가 있는가 — 없으면 용수 줄을 안 만든다.</param>
    /// <param name="floorTrim">바닥면고르기를 적용할 암종(사용자 설정). <c>null</c>이면 토사를 뺀 전부.</param>
    /// <param name="limitM">깊이를 가르는 기준(m). 표 글자와 계산이 <b>같은 값</b>을 써야 한다.</param></summary>
    public static QtyTableSpec Build(IReadOnlyList<RockClass> rocks, bool hasDeep, bool hasWater,
                                     IReadOnlyList<RockClass> floorTrim = null, double limitM = 5.0)
    {
        var rk = Normalize(rocks);
        var depths = hasDeep ? new[] { DepthClass.Le, DepthClass.Gt } : new[] { DepthClass.Le };
        var waters = hasWater ? new[] { WaterClass.Land, WaterClass.Water } : new[] { WaterClass.Land };
        var trim = floorTrim ?? DefaultFloorTrim(rk);

        // ── 왼쪽: 수량 항목 ────────────────────────────────────────────────
        var L = new List<LeftRow>();

        // 성토는 언제나 토사 한 줄이다 — 쌓는 흙은 암종을 안 가린다.
        L.Add(new LeftRow("성    토", null, NameOf(RockClass.Soil), QtyKey.OfFill()));

        // 절토 — 만난 암종만큼.
        for (int i = 0; i < rk.Count; i++)
            L.Add(new LeftRow(i == 0 ? "절    토" : null, null, NameOf(rk[i]), QtyKey.OfCut(rk[i])));

        // 터파기 — 물 × 깊이 × 암종. <b>있는 조건만</b> 줄이 선다.
        bool firstExc = true;
        foreach (var w in waters)
            foreach (var d in depths)
            {
                bool firstOfBlock = true;
                foreach (var r in rk)
                {
                    L.Add(new LeftRow(
                        firstExc ? DepthLabel(d, limitM) : null,
                        firstOfBlock ? $"터파기|({WaterLabel(w)})" : null,
                        NameOf(r),
                        QtyKey.OfExc(r, d, w)));
                    firstExc = false; firstOfBlock = false;
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
        // 바닥면고르기는 <b>사용자가 고른 암종만큼</b> 줄이 선다(JACK).
        for (int i = 0; i < trim.Count; i++)
            R.Add(new RightRow(i == 0 ? "바닥면고르기" : null, NameOf(trim[i])));
        R.Add(new RightRow("식생공법", "성토부"));
        R.Add(new RightRow(null, "절토부"));
        R.Add(new RightRow("층 따 기", null));
        R.Add(new RightRow("잡 석 부 설", null));

        // ── ★★★[JACK 0828] <b>늘고 주는 것은 셋뿐이다.</b>
        //   JACK: <i>"토적표에서 추가되거나 생략되는 건 절토·터파기·바닥면고르기 부분이야.
        //   이 부분들을 제외하고는 그냥 상시 표가 만들어져 있는 걸로 해."</i>
        //   위 코드가 그 규칙 그대로다 — 성토·되메우기와 오른쪽 공종(벌개재근·표토제거·면고르기·
        //   식생공법·층따기·잡석부설)은 <b>현장이 무엇이든 늘 선다</b>.
        //   <see cref="QtyTableSpecRules"/>가 이 약속을 하니스에서 지킨다.
        //
        // ── ★ 두 단의 길이를 맞춘다 — <b>표는 직사각형이라야 한다</b>.
        //   짧은 쪽에 <b>빈 셀</b>을 채운다(JACK 승인: <i>"공백 부분 셀로 표의 우측 부분을 마무리해도 돼"</i>).
        //   병합 계산이 어긋날 자리를 만들지 않는다.
        while (R.Count < L.Count) R.Add(new RightRow(null, null));
        while (L.Count < R.Count) L.Add(new LeftRow(null, null, null, null));

        return new QtyTableSpec(rk, depths, waters, trim, L, R);
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
