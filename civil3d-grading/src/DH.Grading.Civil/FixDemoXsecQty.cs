using System.Windows;
using System.Windows.Media;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1116 «수량표 항목»] 횡단 수량표 오른쪽 칸 미리보기(420 × 300) — 고른 항목마다 «어디의 길이를 재나»를 횡단 위에 칠한다.
/// 정지 그림(<see cref="StaticKeys"/>) · 체크가 바뀌면 패널이 <see cref="XqMask"/>를 넣고 <c>Redraw()</c>. 정의는 Core <c>XsecQtyRight</c>.</summary>
internal sealed partial class FixDemoView
{
    /// <summary>켠 항목(<see cref="XsecQtyOptions"/>의 비트 — 벌개 1 · 표토 2 · 면고르기 4 · 식생 8).</summary>
    public int XqMask { get; set; } = XsecQtyOptions.Default;

    static readonly Brush XqForest = Fz(0x3E, 0x9A, 0x4F), XqStrip = Fz(0xE0, 0x8A, 0x1E), XqFace = Fz(0x2F, 0x6F, 0xD0), XqSeed = Fz(0x7B, 0xC8, 0x5A);

    void XsecQtyScene()
    {
        bool cl = (XqMask & XsecQtyOptions.Clear) != 0, st = (XqMask & XsecQtyOptions.Strip) != 0, sm = (XqMask & XsecQtyOptions.Smooth) != 0,
             sd = (XqMask & XsecQtyOptions.Seed) != 0;
        // 원지반 · 계획면(공사 범위 = 왼쪽 비탈 아래 ~ 오른쪽 성토 사면 끝)
        var g = new[] { new Point(14, 72), new Point(112, 72), new Point(236, 78), new Point(258, 110), new Point(270, 128), new Point(314, 144), new Point(406, 150) };
        var d = new[] { new Point(112, 72), new Point(150, 110), new Point(258, 110), new Point(314, 144) };
        // 흙(계획면 아래 · 원지반 위 = 성토 쪽만 옅게)
        Pl(new[] { new Point(258, 110), new Point(314, 144), new Point(270, 128) }, A(Color.FromRgb(0xF2, 0xE2, 0xC4), 0.9), null, 0);
        Pl(new[] { new Point(112, 72), new Point(150, 110), new Point(258, 110), new Point(236, 78) }, A(Color.FromRgb(0xE8, 0xEE, 0xF4), 0.9), null, 0);
        Pl(g, null, GndLn, 2, closed: false);
        Pl(d, null, Fz(0x33, 0x33, 0x33), 1.6, closed: false);
        TxB(18, 52, "원지반", 10, Dim); TxB(176, 114, "계획면", 10, Txt);
        // 벌개 — 숲 구간 ∩ 공사 범위만 진하게(밖은 옅게)
        if (cl)
        {
            Ln(new Point(40, 66), new Point(180, 66), A(Color.FromRgb(0x3E, 0x9A, 0x4F), 0.28), 6);
            Ln(new Point(286, 140), new Point(332, 146), A(Color.FromRgb(0x3E, 0x9A, 0x4F), 0.28), 6);
            Ln(new Point(112, 66), new Point(180, 66), XqForest, 6);
            Ln(new Point(286, 140), new Point(314, 144), XqForest, 6);
            for (int i = 0; i < 4; i++) { double x = 50 + i * 36; Pl(new[] { new Point(x, 56), new Point(x - 5, 64), new Point(x + 5, 64) }, A(Color.FromRgb(0x3E, 0x9A, 0x4F), 0.45), null, 0); }
        }
        // 표토 — 공사 범위 전체(원지반 선 밑에 가로 띠)
        if (st) Ln(new Point(112, 186), new Point(314, 186), XqStrip, 5);
        if (st) { Ln(new Point(112, 178), new Point(112, 192), XqStrip, 1.5); Ln(new Point(314, 178), new Point(314, 192), XqStrip, 1.5); }
        // 면고르기 · 식생 — 마감 사면(절토 비탈 · 성토 사면)
        if (sm) { Ln(new Point(112, 72), new Point(150, 110), XqFace, 5); Ln(new Point(258, 110), new Point(314, 144), XqFace, 5); }
        if (sd) { Ln(new Point(112, 72), new Point(150, 110), XqSeed, 2.2, true); Ln(new Point(258, 110), new Point(314, 144), XqSeed, 2.2, true); }
        // 이름표
        (string Name, string Tip, Brush Br, bool On)[] rows =
        {
            ("벌개재근", "숲 안 · 공사 범위 가로 길이", XqForest, cl),
            ("표토제거", "공사 범위 전체 가로 길이", XqStrip, st),
            ("면고르기", "마감 사면 빗길이", XqFace, sm),
            ("식생공법", "마감 사면 빗길이", XqSeed, sd),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            double y = 208 + i * 17; var r = rows[i];
            Ln(new Point(16, y + 7), new Point(40, y + 7), r.On ? r.Br : Fz(0xCC, 0xCC, 0xCC), 4);
            TxB(48, y, r.Name, 11, r.On ? Txt : Dim, true);
            TxB(126, y + 1, r.On ? r.Tip : "꺼짐 → 표에 –", 10, r.On ? Txt : Dim);
        }
    }
}
