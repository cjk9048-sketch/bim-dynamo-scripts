// ── S180 ★★[v110.4 · 계획_v110_4.md 5 · 7 M1 · H1 · M3] 진입도로 종단도(부지 [종단도]와 같은 꼴) — 축척 · 검토선 폭 · 도장 · 지우기 · 밴드 묶음 주인 ──
//   만든 값만(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class RoadSheetCheck
{
    static readonly double[] Ladder =
        { 20, 25, 50, 75, 80, 100, 120, 150, 200, 250, 300, 500, 600, 750, 1000, 1200, 1250, 2000, 2500, 3000, 5000 };

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S180 진입도로 종단도(축척 · 폭 · 도장 · 지우기 · 묶음 주인) ==");

        // ① 축척(검토 M1) — 부지 종단뷰가 있으면 부지 축척 · 없으면 고정 · 없으면 1:500
        {
            var a = ProfileSheetRules.RoadScale(true, 200, 0, 0, Ladder);
            var b = ProfileSheetRules.RoadScale(true, 200, 0, 500, Ladder);
            var c = ProfileSheetRules.RoadScale(true, 0, 841.0 * 0.25, 0, Ladder);          // 도곽 폭 210.25 m = 1:250
            var c2 = ProfileSheetRules.RoadScale(true, 0, 841.0 * 0.2493, 0, Ladder);       // 폭이 조금 어긋나도 사다리 1:250
            var d = ProfileSheetRules.RoadScale(false, 0, 0, 300, Ladder);
            var e = ProfileSheetRules.RoadScale(false, 0, 0, 0, Ladder);
            var f = ProfileSheetRules.RoadScale(true, 0, 0, 0, Ladder);                     // 부지 뷰는 있는데 축척 · 도곽 둘 다 못 읽음
            var f2 = ProfileSheetRules.RoadScale(true, 0, 0, 150, Ladder);
            var g = ProfileSheetRules.RoadScale(false, double.NaN, double.NaN, double.NaN, Ladder, double.NaN);
            var h = ProfileSheetRules.RoadScale(false, 120, 0, 0, Ladder);                  // 부지 뷰 없으면 도면 축척은 안 봄
            check("S180 ① 부지 뷰 + 도면 축척 1:200 → 1:200(고정 없음)", a.Scale == 200 && !a.Note.Contains("⚠"), a.Note);
            check("S180 ① 부지 1:200 · 고정 1:500 → 1:200 + 알림(한 도면 한 축척)", b.Scale == 200 && b.Note.Contains("⚠") && b.Note.Contains("500"), b.Note);
            check("S180 ① 도면 축척 0 → 부지 도곽 폭 ÷ 841 mm(사다리에 붙임)", c.Scale == 250 && c2.Scale == 250 && c.Note.Contains("도곽"), $"{c.Scale} · {c2.Scale}");
            check("S180 ① 부지 뷰 없음 · 고정 1:300 → 1:300", d.Scale == 300, d.Note);
            check("S180 ① 부지 뷰 없음 · 고정 없음 → 1:500", e.Scale == 500 && e.Note.Contains("기본"), e.Note);
            check("S180 ① 부지 뷰가 있어도 축척 · 도곽을 못 읽으면 고정 → 기본으로 물러남", f.Scale == 500 && f2.Scale == 150, $"{f.Scale} · {f2.Scale}");
            check("S180 ① 숫자 아님(NaN) → 1:500", g.Scale == 500, g.Note);
            check("S180 ① 부지 뷰가 없으면 도면 축척(남이 건 값)은 안 봄", h.Scale == 500, h.Note);
            check("S180 ① 사다리 붙이기 — 248 → 250 · 1080 → 1000 · 1105 → 1200 · 99 → 100 · 빈 사다리 = 그대로",
                ProfileSheetRules.Snap(248, Ladder) == 250 && ProfileSheetRules.Snap(1080, Ladder) == 1000 && ProfileSheetRules.Snap(1105, Ladder) == 1200 && ProfileSheetRules.Snap(99, Ladder) == 100
                && ProfileSheetRules.Snap(123.4, null) == 123.4, "");
        }

        // ② 검토선 폭 — 가장자리 + 사면 + 여유 5 m → 5 m 올림(정확히 배수면 그대로) · 숫자 아님/음수 = 0
        {
            var w1 = ProfileSheetRules.RoadWidth(3.5, 3.5, 12.3, 0);
            var w2 = ProfileSheetRules.RoadWidth(3.5, 4.0, 6.5, 6.0);
            var w3 = ProfileSheetRules.RoadWidth(double.NaN, -2, double.PositiveInfinity, -1);
            var w4 = ProfileSheetRules.RoadWidth(3.0, 3.0, 40.2, 0.1, 5, 0);                 // 간격 0 → 5
            check("S180 ② 3.5 + 12.3 + 5 = 20.8 → 25 · 3.5 + 0 + 5 → 10", w1 == (25, 10), $"{w1}");
            check("S180 ② 정확히 배수(3.5 + 6.5 + 5 = 15) → 15 · 4 + 6 + 5 = 15 → 15", w2 == (15, 15), $"{w2}");
            check("S180 ② 숫자 아님 · 음수 → 여유만(5)", w3 == (5, 5), $"{w3}");
            check("S180 ② 간격 0 → 5 m 단위", w4 == (50, 10), $"{w4}");
        }

        // ③ 도장 — 같은 값 = 같은 도장 · 1 mm 바뀜 · 순서 · 끝 측점 · PVI 수가 바뀌면 다름 · 0.4 mm는 같음(반올림)
        {
            var p = new List<(double, double)> { (0, 100), (60, 104.5), (120, 103.2) };
            string s0 = ProfileSheetRules.Stamp(p, 0, 120);
            string sSame = ProfileSheetRules.Stamp(new List<(double, double)>(p), 0, 120);
            string sZ = ProfileSheetRules.Stamp(new List<(double, double)> { (0, 100), (60, 104.501), (120, 103.2) }, 0, 120);
            string sTiny = ProfileSheetRules.Stamp(new List<(double, double)> { (0, 100), (60, 104.5004), (120, 103.2) }, 0, 120);
            string sOrd = ProfileSheetRules.Stamp(new List<(double, double)> { (60, 104.5), (0, 100), (120, 103.2) }, 0, 120);
            string sEnd = ProfileSheetRules.Stamp(p, 0, 121);
            string sN = ProfileSheetRules.Stamp(new List<(double, double)> { (0, 100), (120, 103.2) }, 0, 120);
            string sNull = ProfileSheetRules.Stamp(null, 0, 120);
            check("S180 ③ 같은 PVI = 같은 도장(16자)", s0 == sSame && s0.Length == 16, s0);
            check("S180 ③ 높이 1 mm · 순서 · 끝 측점 · PVI 수가 바뀌면 다른 도장", sZ != s0 && sOrd != s0 && sEnd != s0 && sN != s0 && sNull != s0, $"{sZ} {sOrd} {sEnd} {sN}");
            check("S180 ③ 0.4 mm(반올림 안)는 같은 도장", sTiny == s0, sTiny);
        }

        // ④ 지우기 표(검토 M3 · 계획 4) — 부지 길 = 표식 없는 것만 · 도로 길 = 이 노선 표식만
        {
            var rows = new (bool Road, string? Tag, string? Me, bool Want, string Why)[]
            {
                (false, null, null, true, "부지 · 표식 없음 → 지움(지금 그대로)"),
                (false, "A1", null, false, "부지 · 도로 표식 → 안 지움"),
                (false, "", null, false, "부지 · 빈 표식(표식은 있음) → 안 지움"),
                (true, null, "A1", false, "도로 · 표식 없음(부지 것) → 안 지움"),
                (true, "A1", "A1", true, "도로 · 이 노선 → 지움"),
                (true, "a1", "A1", true, "도로 · 핸들 대소문자 → 지움"),
                (true, "B2", "A1", false, "도로 · 다른 도로 → 안 지움"),
                (true, "A1", "", false, "도로 · 내 핸들 없음 → 안 지움"),
                (true, "", "", false, "도로 · 둘 다 빈 → 안 지움"),
            };
            var bad = rows.Where(r => ProfileSheetRules.ShouldErase(r.Road, r.Tag, r.Me) != r.Want).Select(r => r.Why).ToList();
            check($"S180 ④ 지우기 판정 {rows.Length}줄", bad.Count == 0, string.Join(" · ", bad));
        }

        // ⑤ 밴드 묶음 주인(검토 H1) — 부지 길: 도로 도곽 안이면 뺌 · 도로 길: 그 뷰 밴드 상자 안만
        {
            var road = new[] { new ProfileSheetRules.Rect(1000, 0, 1420.5, 297), new ProfileSheetRules.Rect(1420.5, 0, 1841, 297) };
            check("S180 ⑤ 부지 길 — 도로 도곽이 없으면 늘 부지 것", ProfileSheetRules.SiteOwnsGroup(1200, 50, null) && ProfileSheetRules.SiteOwnsGroup(1200, 50, Array.Empty<ProfileSheetRules.Rect>()), "");
            check("S180 ⑤ 부지 길 — 도로 도곽 안(둘째 장 포함) = 뺌 · 밖(위 · 왼쪽) = 부지 것",
                !ProfileSheetRules.SiteOwnsGroup(1200, 50, road) && !ProfileSheetRules.SiteOwnsGroup(1600, 10, road)
                && ProfileSheetRules.SiteOwnsGroup(1200, 400, road) && ProfileSheetRules.SiteOwnsGroup(900, 50, road), "");
            // 도로 뷰: 그래프 아래 끝 Y = 100 · 밴드 모형 높이 60 → 상자 Y 40~100(± 여유 1)
            bool inBox = ProfileSheetRules.RoadOwnsGroup(1100, 70, 1050, 1350, 100, 60, 1);
            bool inGraph = ProfileSheetRules.RoadOwnsGroup(1100, 130, 1050, 1350, 100, 60, 1);
            bool below = ProfileSheetRules.RoadOwnsGroup(1100, 30, 1050, 1350, 100, 60, 1);
            bool sideX = ProfileSheetRules.RoadOwnsGroup(1400, 70, 1050, 1350, 100, 60, 1);
            bool edge = ProfileSheetRules.RoadOwnsGroup(1350.5, 99.5, 1050, 1350, 100, 60, 1);
            check("S180 ⑤ 도로 길 — 밴드 상자 안 = 내 것 · 그래프 · 아래 · 옆 = 아님 · 가장자리(여유 안) = 내 것", inBox && !inGraph && !below && !sideX && edge,
                $"{inBox} {inGraph} {below} {sideX} {edge}");
        }

        // ⑥ 놓을 자리 겹침(검토 H1) — 부지 도곽 바로 위 · 아래(같은 X)는 겹침 · 옆으로 비키면 안 겹침 · 맞닿음 = 안 겹침
        {
            double s = 200 / 1000.0;
            double siteX0 = 0, siteX1 = 841 * s;                           // 부지 한 장(모형 168.2 m)
            var up = ProfileSheetRules.RoadSheetsSpan(30, 200, 1);          // 같은 X에서 위로
            var right = ProfileSheetRules.RoadSheetsSpan(siteX1 + 130 * s + 1, 200, 2);
            var touch = ProfileSheetRules.RoadSheetsSpan(siteX1 + 130 * s, 200, 1);
            check("S180 ⑥ 같은 X(위 · 아래) = 겹침 · 옆 = 안 겹침 · 맞닿음 = 안 겹침",
                ProfileSheetRules.SpansOverlap(siteX0, siteX1, up.X0, up.X1) && !ProfileSheetRules.SpansOverlap(siteX0, siteX1, right.X0, right.X1)
                && !ProfileSheetRules.SpansOverlap(siteX0, siteX1, touch.X0, touch.X1),
                $"위 {up.X0:F1}~{up.X1:F1} · 옆 {right.X0:F1}~{right.X1:F1}");
            var two = ProfileSheetRules.RoadSheetsSpan(0, 500, 3);
            check("S180 ⑥ 1:500 세 장 = 841 mm × 3 × 0.5 + 왼쪽 65 m", Math.Abs(two.X1 - 1261.5) < 1e-9 && Math.Abs(two.X0 + 65) < 1e-9, $"{two}");
        }
    }
}
