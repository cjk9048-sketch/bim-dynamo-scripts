// ── S171 ★★[v104.1 · 계획_v104_1.md 2.2 · JACK 1022 «종단 · 횡단에 옹벽 단면을 직접 넣어줘» · 2차 «각도대로 늘여서 맞춤»] 자른 자리 단면 ──
//   곧은 벽 직각 · 거꾸로 · 45°(1/sin 늘임) · 10°(빠짐 · 셈) · 안 만남 · 3단 거리순 · 꼭짓점 통과 하나 · 단면 = 수량 단면(가까운 끝점 · 앞 땅만 보간)
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallCutCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S171 자른 자리 옹벽 단면(종단 · 횡단 그림) ==");
        var t = WallSection.DefaultTable(WallSection.Kind.역T형);
        int row = t.Rows.FindIndex(r => Math.Abs(r[0] - 6.0) < 1e-9);
        var c = new WallSection.Common(0.10, 0.10, 0.02, 0.01);
        WallExcav.SecRecord Rec(params (double Y, int Soil)[] lines)
        {
            var rec = new WallExcav.SecRecord { Margin = 0.5, Common = c };
            rec.Tables[WallSection.Kind.역T형] = t;
            foreach (var (y, soil) in lines)
                rec.Lines.Add(new WallExcav.RecLine
                {
                    Kind = WallSection.Kind.역T형, Soil = soil, Up = true, SlopeW = 0.01,
                    // 앞 땅 100 → 102(x 0 → 20) · 꼭짓점 x 10
                    Pts = new() { new(0, y, 100, 6.0, 1.0, row), new(10, y, 101, 6.0, 1.0, row), new(20, y, 102, 6.0, 1.0, row) },
                });
            return rec;
        }
        var shRef = WallSection.BuildRow(WallSection.Kind.역T형, t.Rows[row], 6.0, c with { GradeFace = 0.01 }, true, 1.0);
        const double MinSin = 0.25;

        // ① 직각 · 흙 쪽(+y)으로 자름 — 하나 · 거리 5 · 부호 + · 늘임 1 · 앞 땅 x 5 → 100.5
        var r1 = WallExcav.CutSections(Rec((0, +1)), 5, -5, 5, 10, MinSin, out int sk1);
        check("S171 ① 직각 자름 — 하나 · 거리 5 · 흙 쪽 + · 늘임 1 · 앞 땅 보간 100.5 · 단면 = 수량 단면",
            r1.Count == 1 && sk1 == 0 && Math.Abs(r1[0].Dist - 5) < 1e-9 && r1[0].Sign == 1 && Math.Abs(r1[0].Stretch - 1) < 1e-9 && Math.Abs(r1[0].Toe - 100.5) < 1e-9
            && Math.Abs(r1[0].Shape.XBack - shRef.XBack) < 1e-9 && Math.Abs(r1[0].Shape.XFront - shRef.XFront) < 1e-9 && r1[0].Shape.Parts.Count == shRef.Parts.Count,
            r1.Count == 0 ? "없음" : $"{r1[0].Dist:F3} · {r1[0].Sign} · {r1[0].Stretch:F3} · {r1[0].Toe:F3} · 뒤 {r1[0].Shape.XBack:F3}/{shRef.XBack:F3}");
        // ② 거꾸로 자름 — 부호 − · 거리 10
        var r2 = WallExcav.CutSections(Rec((0, +1)), 5, 10, 5, -5, MinSin, out _);
        check("S171 ② 흙 쪽 반대로 자름 — 부호 − · 거리 10", r2.Count == 1 && r2[0].Sign == -1 && Math.Abs(r2[0].Dist - 10) < 1e-9, r2.Count == 0 ? "없음" : $"{r2[0].Sign} · {r2[0].Dist:F3}");
        // ③ 45° — 교점 (5,0) · 거리 5√2 · 늘임 √2
        var r3 = WallExcav.CutSections(Rec((0, +1)), 0, -5, 10, 5, MinSin, out _);
        check("S171 ③ 45° 자름 — 거리 5√2 · 늘임 √2(각도대로 늘여 땅선과 맞춤)",
            r3.Count == 1 && Math.Abs(r3[0].Dist - 5 * Math.Sqrt(2)) < 1e-9 && Math.Abs(r3[0].Stretch - Math.Sqrt(2)) < 1e-9 && r3[0].Sign == 1,
            r3.Count == 0 ? "없음" : $"{r3[0].Dist:F4} · {r3[0].Stretch:F4}");
        // ④ 10° — sin 0.17 &lt; 0.25 → 빠짐 · 셈 1
        double a10 = 10 * Math.PI / 180;
        var r4 = WallExcav.CutSections(Rec((0, +1)), 5 - 5 * Math.Cos(a10), -5 * Math.Sin(a10), 5 + 5 * Math.Cos(a10), 5 * Math.Sin(a10), MinSin, out int sk4);
        check("S171 ④ 10° 자름(벽과 거의 나란함) — 안 그림 · 빠진 수 1", r4.Count == 0 && sk4 == 1, $"{r4.Count} · 빠짐 {sk4}");
        // ⑤ 안 만남
        var r5 = WallExcav.CutSections(Rec((0, +1)), 25, -5, 25, 5, MinSin, out int sk5);
        check("S171 ⑤ 줄 끝 너머 — 없음", r5.Count == 0 && sk5 == 0, $"{r5.Count} · {sk5}");
        // ⑥ 3단(y 0 · 2 · 4) — 셋 · 거리순 5 · 7 · 9 · 줄 번호 0 · 1 · 2
        var r6 = WallExcav.CutSections(Rec((0, +1), (4, +1), (2, +1)), 5, -5, 5, 10, MinSin, out _);
        check("S171 ⑥ 3단 — 셋 · 거리순", r6.Count == 3 && Math.Abs(r6[0].Dist - 5) < 1e-9 && Math.Abs(r6[1].Dist - 7) < 1e-9 && Math.Abs(r6[2].Dist - 9) < 1e-9
            && r6[0].Line == 0 && r6[1].Line == 2 && r6[2].Line == 1, string.Join(" ", r6.Select(x => $"{x.Dist:F1}/{x.Line}")));
        // ⑦ 꼭짓점(x 10) 통과 — 하나 · 앞 땅 101
        var r7 = WallExcav.CutSections(Rec((0, +1)), 10, -5, 10, 10, MinSin, out _);
        check("S171 ⑦ 꼭짓점 통과 — 하나 · 앞 땅 101", r7.Count == 1 && Math.Abs(r7[0].Toe - 101) < 1e-9, $"{r7.Count} · {(r7.Count > 0 ? r7[0].Toe : double.NaN):F3}");
        // ⑨ 꺾은선(종단 — 선형을 편 것) · 꼭짓점이 벽 위에 있어도 하나 · 변 번호 · 비율
        var r9 = WallExcav.CutSections(Rec((0, +1)), new[] { (5.0, -5.0), (5.0, -1.0), (5.0, 10.0) }, MinSin, out _);
        var r9b = WallExcav.CutSections(Rec((0, +1)), new[] { (5.0, -5.0), (5.0, 0.0), (5.0, 10.0) }, MinSin, out _);
        check("S171 ⑨ 꺾은선 자름 — 거리 5 · 둘째 변 · 비율 1/11 / 꺾은선 꼭짓점이 벽 위 — 하나",
            r9.Count == 1 && Math.Abs(r9[0].Dist - 5) < 1e-9 && r9[0].Seg == 1 && Math.Abs(r9[0].SegT - 1.0 / 11) < 1e-9 && r9b.Count == 1 && Math.Abs(r9b[0].Dist - 5) < 1e-9,
            $"{r9.Count} · {(r9.Count > 0 ? $"{r9[0].Dist:F3}/{r9[0].Seg}/{r9[0].SegT:F4}" : "")} · 꼭짓점 {r9b.Count}");
        // ⑧ 흙 쪽이 −y인 줄(Soil −1)을 +y로 자름 — 부호 −
        var r8 = WallExcav.CutSections(Rec((0, -1)), 5, -5, 5, 10, MinSin, out _);
        check("S171 ⑧ 흙 쪽 −1 줄 — 부호 −", r8.Count == 1 && r8[0].Sign == -1, r8.Count == 0 ? "없음" : $"{r8[0].Sign}");
    }
}
