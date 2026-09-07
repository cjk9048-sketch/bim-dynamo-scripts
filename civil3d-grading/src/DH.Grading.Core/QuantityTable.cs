namespace DH.Grading.Core;

/// <summary>★★★[검토 0907] <b>도면이 읽는 몇 가지만 남은 자리.</b>
///
/// <para>0827에 스크린샷을 옮겨 적은 <b>참고표</b>가 여기 있었다(12줄 7칸 고정).
/// 0831부터 표는 <see cref="QtyTableSpec"/>가 <b>현장의 지층 구성</b>에서 지어 내고(§57),
/// 0907부터 <see cref="QtyTableFold"/>가 그것을 <b>두 단</b>으로 나눈다(§72).</para>
///
/// <para>그래서 이 클래스에는 <b>표 모양이 없다</b> — 도면이 실제로 읽는 넷만 남았다:
/// 빈칸 글자·머리줄 글자·깊이 기준·칸 폭 비율.</para></summary>
public static class QuantityTable
{
    /// <summary>값이 없는 칸에 적는 글자. <b>빈칸으로 두지 않는다</b> —
    /// 빈칸은 "아직 안 넣었다"로 읽히고, <c>–</c>는 "해당 없음"으로 읽힌다(JACK 지시).</summary>
    public const string Blank = "–";

    /// <summary>표 맨 윗줄. 측점 이름이 뒤에 붙는다.</summary>
    public const string HeaderLeft = "측  점";

    /// <summary>★[JACK 0827] 터파기 깊이 구분(m). 표에 적히는 글자와
    /// 계산 쪽(<c>XsecQuantity.DeepLimit</c>)이 <b>같은 값</b>을 써야 어긋나지 않는다.</summary>
    public const double DeepLimitM = 5.0;

    // ★★★[검토 0907 · L-5] <b>참고표(12줄 못 박은 <c>Rows</c> 배열)를 지웠다.</b>
    //
    //   0827에 스크린샷을 그대로 옮겨 적은 표다. 0831에 <see cref="QtyTableSpec"/>가
    //   <b>현장의 지층 구성</b>에서 표를 지어 내게 되면서(§57) 도면은 그것만 그린다 —
    //   이 배열은 그 뒤로 <b>한 번도 안 그려졌다</b>. 그런데 시험대가 열두 개 검사로
    //   이것을 재며 통과 도장을 찍고 있었다(S69·S73).
    //
    //   <b>지운 것</b>: <c>Rows</c>·<c>Cell</c>·<c>Row</c>·<c>QtyKind</c>·<c>BodyRows</c>·
    //   <c>TotalRows</c>·<c>Cols</c>·<c>DepthRow</c>·<c>DepthLabel</c>·<c>L1TextOf</c>·
    //   <c>SpansValid</c>·<c>PickLeft</c>·<c>PickRight</c>·<c>FilledSlots</c>.
    //   <b>남긴 것</b>: <c>Blank</c>·<c>HeaderLeft</c>·<c>DeepLimitM</c>·<c>ColRatio</c> —
    //   도면이 실제로 읽는 넷이다.
    //
    //   깊이 딱지가 필요하면 <c>QtyTableSpec.DepthLabel(깊이, 기준m)</c>을 쓴다.

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
    /// <summary>★★★[검토 0907 · M-1] <b>옛 7칸 판을 위한 것이다 — 지금 도면은 안 쓴다.</b>
    /// <para>단이 둘이 된 뒤로 도면은 <see cref="ColRatio"/>의 <b>0~3번만</b> 읽는다
    /// (<c>QtyTableFold.ColRatioIndex</c>가 두 단 모두 <c>{0,1,2,3}</c>을 준다).
    /// 4~6번(E·F·G)은 <b>한 곳에서도 안 쓰인다</b>.</para>
    /// <para>그래서 <b>이 검사를 로그와 시험대에서 뺐다</b> — 맞는 도면에 "어긋남"이라 말할 수 있었다.
    /// 지금 쓰는 검사는 <c>QtyTableFold.PanelsAligned</c>다.
    /// 남겨 두는 이유는 하나 — 0~3번 값이 <b>옛 E·F·G와 같은 폭</b>이라는 내력을 적어 두기 위해서다.
    /// 그 덕에 얼개를 통째로 바꾸고도 표 폭이 안 흔들렸다(§72).</para></summary>
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
