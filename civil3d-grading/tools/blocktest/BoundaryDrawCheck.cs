// ── S208 ★★[v113.0 · JACK 1104 «[옹벽 추가] — 첫 점 경계 위 · 가운데 경계 안 · 끝 점 다시 경계 위»] 찍는 동안 점마다 규칙(BoundaryDraw) ──
using DH.Grading.Core;

static class BoundaryDrawCheck
{
    static List<Point3> L(params (double X, double Y)[] p) => p.Select(q => new Point3(q.X, q.Y, 0)).ToList();

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S208 옹벽 추가 — 찍는 규칙(BoundaryDraw) ==");
        // 네모 경계 0..100 · 바닥(계획선) 40..60
        var sq = L((0, 0), (100, 0), (100, 100), (0, 100));
        var fl = L((40, 40), (60, 40), (60, 60), (40, 60));
        // U 꼴 경계(가운데 홈 30..70 · y ≥ 30) · 바닥 40..60 × 5..20
        var u = L((0, 0), (100, 0), (100, 100), (70, 100), (70, 30), (30, 30), (30, 100), (0, 100));
        var ufl = L((40, 5), (60, 5), (60, 20), (40, 20));
        const double snap = 1.0;

        var f1 = BoundaryDraw.First(sq, fl, 0.3, 50, snap);
        check("S208 ① 첫 점 — 경계 0.3 m 곁 → 경계 위로 붙임", f1.Kind == BoundaryDraw.Kind.Start && Math.Abs(f1.X) < 1e-12 && Math.Abs(f1.Y - 50) < 1e-12, $"{f1.Kind} ({f1.X:F4}, {f1.Y:F4}) {f1.Why}");
        var f2 = BoundaryDraw.First(sq, fl, 10, 50, snap);
        check("S208 ② 첫 점 — 경계 밖 10 m(안쪽) → 거절", f2.Kind == BoundaryDraw.Kind.No && f2.Why.Contains("경계 위"), f2.Why);
        var flB = L((40, 0), (60, 0), (60, 20), (40, 20));
        var f3 = BoundaryDraw.First(sq, flB, 50, 0.2, snap);
        check("S208 ③ 첫 점 — 바닥에 붙은 경계 자리 → 거절", f3.Kind == BoundaryDraw.Kind.No && f3.Why.Contains("바닥"), f3.Why);

        var st = new List<(double X, double Y)> { (0, 50) };
        var n1 = BoundaryDraw.Next(sq, fl, st, 20, 30, snap);
        check("S208 ④ 가운데 점 — 경계 안 → 받음", n1.Kind == BoundaryDraw.Kind.Inside, $"{n1.Kind} {n1.Why}");
        var n2 = BoundaryDraw.Next(sq, fl, st, -5, 50, snap);
        check("S208 ⑤ 가운데 점 — 경계 밖(5 m) → 거절", n2.Kind == BoundaryDraw.Kind.No && n2.Why.Contains("경계 밖"), n2.Why);

        var us = new List<(double X, double Y)> { (0, 80), (15, 50) };
        var n3 = BoundaryDraw.Next(u, ufl, us, 85, 50, snap);
        check("S208 ⑥ 선이 홈(경계)을 지남 → 거절 · 자리", n3.Kind == BoundaryDraw.Kind.No && n3.Why.Contains("지남") && n3.At != null && (Math.Abs(n3.At.Value.X - 30) < 1e-6 || Math.Abs(n3.At.Value.X - 70) < 1e-6) && Math.Abs(n3.At.Value.Y - 50) < 1e-6, $"{n3.Why} @{n3.At}");
        var us2 = new List<(double X, double Y)> { (0, 80), (15, 25) };
        var n4 = BoundaryDraw.Next(u, ufl, us2, 100.3, 26, snap);
        check("S208 ⑦ 끝 점 — 경계 0.3 m 곁 → 붙여 끝", n4.Kind == BoundaryDraw.Kind.End && Math.Abs(n4.X - 100) < 1e-12 && Math.Abs(n4.Y - 26) < 1e-12, $"{n4.Kind} ({n4.X:F4}, {n4.Y:F4}) {n4.Why}");
        var n5 = BoundaryDraw.Next(u, ufl, us, 0.3, 79.5, snap);
        check("S208 ⑧ 끝 점 — 시작 자리와 1 m 안 → 거절", n5.Kind == BoundaryDraw.Kind.No && n5.Why.Contains("시작"), n5.Why);
        var us3 = new List<(double X, double Y)> { (0, 80), (15, 15) };
        var n6 = BoundaryDraw.Next(u, ufl, us3, 85, 15, snap);
        check("S208 ⑨ 선이 바닥을 지남 → 거절", n6.Kind == BoundaryDraw.Kind.No && n6.Why.Contains("바닥"), n6.Why);
        var us4 = new List<(double X, double Y)> { (0, 90), (25, 40), (10, 40) };
        var n7 = BoundaryDraw.Next(u, ufl, us4, 20, 60, snap);
        check("S208 ⑩ 앞 선과 엇갈림 → 거절 · 자리(17.5, 55)", n7.Kind == BoundaryDraw.Kind.No && n7.Why.Contains("엇갈") && n7.At != null && Math.Abs(n7.At.Value.X - 17.5) < 1e-6 && Math.Abs(n7.At.Value.Y - 55) < 1e-6, $"{n7.Why} @{n7.At}");
        var n8 = BoundaryDraw.Next(u, ufl, us4, 10.2, 40.1, snap);
        check("S208 ⑪ 앞 점과 0.5 m 안 → 거절", n8.Kind == BoundaryDraw.Kind.No && n8.Why.Contains("앞 점"), n8.Why);
        // 끝까지 가는 선이 경계를 먼저 지나면(홈 건너 오른쪽 경계) 거절
        var n9 = BoundaryDraw.Next(u, ufl, us, 100.2, 60, snap);
        check("S208 ⑫ 끝 점까지 가는 선이 경계를 먼저 지남 → 거절", n9.Kind == BoundaryDraw.Kind.No && n9.Why.Contains("먼저"), n9.Why);
        // 가운데 점이 경계에 snap 안 → 끝 점으로(저절로 끝)
        var n10 = BoundaryDraw.Next(sq, fl, st, 20, 99.5, snap);
        check("S208 ⑬ 경계 곁 클릭 = 끝 점(저절로 끝)", n10.Kind == BoundaryDraw.Kind.End && Math.Abs(n10.Y - 100) < 1e-12, $"{n10.Kind} ({n10.X:F3}, {n10.Y:F3})");

        // ⑭ 다 그린 점 → 늘여 판정(Classify) = 통과 · A · B = 경계 위 두 점 · 바닥 쪽이 남음
        var drawn = new List<(double X, double Y)> { (0, 80) };
        var s1 = BoundaryDraw.Next(u, ufl, drawn, 15, 25, snap); drawn.Add((s1.X, s1.Y));
        var s2 = BoundaryDraw.Next(u, ufl, drawn, 85, 25, snap);
        // (15,25) → (85,25)는 바닥(5..20) 위 5 m · 홈(y ≥ 30) 아래 — 받음
        if (s2.Ok) drawn.Add((s2.X, s2.Y));
        var s3 = BoundaryDraw.Next(u, ufl, drawn, 100.4, 50, snap);
        if (s3.Ok) drawn.Add((s3.X, s3.Y));
        var line = BoundaryDraw.Extend(drawn, 1.0);
        var cut = BoundaryCut.Classify(line, u, ufl);
        bool okAB = cut.Ok && Math.Abs(cut.A.X - 0) < 1e-6 && Math.Abs(cut.A.Y - 80) < 1e-6 && Math.Abs(cut.B.X - 100) < 1e-6 && Math.Abs(cut.B.Y - 50) < 1e-6;
        check("S208 ⑭ 그린 점(경계 위 → 안 → 안 → 경계 위) → 늘여 판정 통과 · 교점 = 찍은 경계 점", s1.Ok && s2.Ok && s3.Kind == BoundaryDraw.Kind.End && okAB,
              $"{s1.Kind}/{s2.Kind}/{s3.Kind} {s2.Why}{s3.Why} · 판정 {BoundaryCut.CodeName(cut.Code)} {cut.Why} · A({cut.A.X:F3},{cut.A.Y:F3}) B({cut.B.X:F3},{cut.B.Y:F3}) · 교차 {cut.Crossings}");
        // ⑮ 경계 꼭짓점에 붙인 첫 점(모서리) — 첫 토막이 모서리에서 안으로
        var f4 = BoundaryDraw.First(sq, fl, 0.2, 0.3, snap);
        var n11 = BoundaryDraw.Next(sq, fl, new List<(double X, double Y)> { (f4.X, f4.Y) }, 10, 10, snap);
        // ⑯ 기운 3D 화면 — 높이 100 m 자리에서 찍힌 점을 시선(0.5, −0.8, 0.3)을 따라 Z = 0으로 · 위에서 본 화면은 그대로
        var fl1 = BoundaryDraw.Flat(10, 20, 100, 0.5, -0.8, 0.3);
        double ex1 = 10 + 0.5 * (-100 / 0.3), ey1 = 20 - 0.8 * (-100 / 0.3);
        var fl2 = BoundaryDraw.Flat(10, 20, 100, 0, 0, 1);
        var fl3 = BoundaryDraw.Flat(10, 20, 0, 0.5, -0.8, 0.3);
        check("S208 ⑯ 기운 화면 점 → 시선 따라 경계 평면(Z = 0) · 위에서 본 화면 · Z = 0은 그대로", Math.Abs(fl1.X - ex1) < 1e-9 && Math.Abs(fl1.Y - ey1) < 1e-9 && fl2 == (10.0, 20.0) && fl3 == (10.0, 20.0),
              $"기운 ({fl1.X:F3}, {fl1.Y:F3}) / 기대 ({ex1:F3}, {ey1:F3}) · 위 {fl2} · Z0 {fl3}");
        // ⑰ [설치 전 검토 3] 경계 밖 클릭 → 앞 점에서 그쪽으로 가는 선이 처음 경계를 지나는 자리에서 끝
        var us5 = new List<(double X, double Y)> { (0, 80), (15, 25) };
        var n12 = BoundaryDraw.Next(u, ufl, us5, 110, 26, snap);
        double ey17 = 25 + (100 - 15) / (110.0 - 15) * 1;
        check("S208 ⑰ 경계 밖 클릭 → 선이 경계를 지나는 자리(100, 25.895)에서 끝", n12.Kind == BoundaryDraw.Kind.End && Math.Abs(n12.X - 100) < 1e-9 && Math.Abs(n12.Y - ey17) < 1e-9, $"{n12.Kind} ({n12.X:F4}, {n12.Y:F4}) {n12.Why}");
        // ⑱ [설치 전 검토 3] 붙임 거리(snap)가 커도 가운데 클릭은 0.5 m 밖이면 가운데 점(좁은 사면 띠에서 끝나지 않게)
        var n13 = BoundaryDraw.Next(sq, fl, st, 20, 99.2, 5.0);
        var n14 = BoundaryDraw.Next(sq, fl, st, 20, 99.6, 5.0);
        check("S208 ⑱ snap 5 m — 경계 0.8 m 안쪽 클릭 = 가운데 점 · 0.4 m = 끝", n13.Kind == BoundaryDraw.Kind.Inside && n14.Kind == BoundaryDraw.Kind.End, $"0.8 m {n13.Kind} · 0.4 m {n14.Kind}");
        // ⑲ [설치 전 검토 4] 한 번 지은 경계(Prepare)에 붙임 — 고친 고리(가시 빠짐)에 붙인다 · 같은 판정
        var spike = L((0, 0), (100, 0), (100, 50), (130, 50), (100, 50), (100, 100), (0, 100));
        var cx19 = BoundaryDraw.Prepare(spike, null);
        var f19 = cx19 == null ? null : BoundaryDraw.First(cx19, 125, 50.2, snap);
        var cxq = BoundaryDraw.Prepare(sq, fl);
        var same19 = cxq != null && BoundaryDraw.Next(cxq, st, 20, 30, snap).Kind == n1.Kind && BoundaryDraw.Next(cxq, st, -5, 50, snap).Kind == n2.Kind;
        check("S208 ⑲ 준비한 경계 — 가시(유효하지 않은 고리)는 고친 고리로 · 가시 끝 곁 클릭 거절 · 준비한 판정 = 한 번 판정", cx19 != null && f19 != null && f19.Kind == BoundaryDraw.Kind.No && same19,
              $"고리 점 {cx19?.RingCount} · 가시 끝 곁 {f19?.Kind} {f19?.Why} · 같음 {same19}");
        check("S208 ⑮ 모서리 곁 첫 점 → 붙임 · 안쪽 토막 받음", f4.Kind == BoundaryDraw.Kind.Start && n11.Kind == BoundaryDraw.Kind.Inside, $"{f4.Kind} ({f4.X:F3},{f4.Y:F3}) → {n11.Kind} {n11.Why}");

        // ⑳ ★[v117.2 · JACK 1111 13:5x «폴리선의 끝 점이 경계 안» — 경계 V 자리에서 끝을 1 m 눈감고 늘이면 경계 안으로] ExtendSafe — 늘인 점이 경계 밖이고 경계에 끝 점에서만 닿을 때만 늘임
        {
            var cxU = BoundaryDraw.Prepare(u, ufl)!;
            var safe14 = BoundaryDraw.ExtendSafe(cxU, drawn, 1.0, out bool ea14, out bool eb14);
            bool same14 = ea14 && eb14 && safe14.Count == line.Count && safe14.Zip(line).All(q => q.First.X == q.Second.X && q.First.Y == q.Second.Y);
            var cut14 = BoundaryCut.Classify(safe14, u, ufl);
            check("S208 ⑳ 곧은 경계(⑭ 그대로) — 두 끝 다 늘임 · 점이 Extend와 비트 같음 · 판정 같음", same14 && cut14.Ok == cut.Ok && cut14.A.X == cut.A.X && cut14.A.Y == cut.A.Y && cut14.B.X == cut.B.X && cut14.B.Y == cut.B.Y,
                  $"늘임 A {ea14} · B {eb14} · 점 {safe14.Count}/{line.Count} · 같음 {same14} · {BoundaryCut.CodeName(cut14.Code)}");
            // V 홈(위 변 가운데 (40,100) → (50,90) → (60,100)) · 바닥 40..60 × 20..40
            var vr = L((0, 0), (100, 0), (100, 100), (60, 100), (50, 90), (40, 100), (0, 100));
            var vfl = L((40, 20), (60, 20), (60, 40), (40, 40));
            var cxV = BoundaryDraw.Prepare(vr, vfl)!;
            foreach (var (nm, dpts, vEnd) in new[]
            {
                ("끝이 V 꼭짓점", new List<(double X, double Y)> { (0, 50), (20, 85), (50, 90) }, true),
                ("시작이 V 꼭짓점", new List<(double X, double Y)> { (50, 90), (80, 85), (100, 50) }, false),
            })
            {
                // 찍는 규칙도 받는지(첫 점 · 가운데 · 끝)
                var acc = new List<(double X, double Y)>(); bool drawOk = true; string dw = "";
                for (int i = 0; i < dpts.Count; i++)
                {
                    var stp = i == 0 ? BoundaryDraw.First(cxV, dpts[i].X, dpts[i].Y, snap) : BoundaryDraw.Next(cxV, acc, dpts[i].X, dpts[i].Y, snap);
                    dw += $"{stp.Kind} ";
                    if (!stp.Ok) { drawOk = false; dw += stp.Why; break; }
                    acc.Add((stp.X, stp.Y));
                }
                var old = BoundaryCut.Classify(BoundaryDraw.Extend(acc, 1.0), vr, vfl);
                var sl = BoundaryDraw.ExtendSafe(cxV, acc, 1.0, out bool ea, out bool eb);
                var cv = BoundaryCut.Classify(sl, vr, vfl);
                var vEndPt = vEnd ? cv.B : cv.A;
                bool ok = drawOk && !old.Ok && cv.Ok && (vEnd ? ea && !eb : !ea && eb) && Math.Abs(vEndPt.X - 50) < 1e-9 && Math.Abs(vEndPt.Y - 90) < 1e-9;
                check($"S208 ⑳ {nm} — 옛 늘임은 거절 · 그 끝은 안 늘이고 클릭 점(V) 그대로 → 판정 통과 · 교점 = V", ok,
                      $"찍기 {dw}· 옛 {BoundaryCut.CodeName(old.Code)} {old.Why} · 새 늘임 A {ea} · B {eb} · {BoundaryCut.CodeName(cv.Code)} {cv.Why} · A({cv.A.X:F4},{cv.A.Y:F4}) B({cv.B.X:F4},{cv.B.Y:F4}) · 교차 {cv.Crossings}");
            }
        }

        // ㉑ ★[v117.14.1 · JACK 1112 «부지에 겹치면 겹치는 부분 빨간 해치 — 지금은 빨간 선 + X»] 거절 까닭이 부지 바닥이면 Floor 표지(부르는 쪽이 X 대신 겹침 칠) · 다른 까닭은 아님
        check("S208 ㉑ 바닥 거절만 Floor 표지 — 첫 점 바닥(③) · 선이 바닥 지남(⑨) = 참 · 홈 지남(⑥) · 엇갈림(⑩) · 경계 밖(⑤) = 거짓",
              f3.Floor && n6.Floor && !n3.Floor && !n7.Floor && !n2.Floor && !n1.Floor, $"③ {f3.Floor} · ⑨ {n6.Floor} · ⑥ {n3.Floor} · ⑩ {n7.Floor} · ⑤ {n2.Floor} · ④ {n1.Floor}");
        // ㉒ ★[v117.14.1 · JACK 1112 «미리보기 어디든 생기게»] 반직선이 경계를 처음 지나는 자리(커서 뒤를 경계까지 임시로 잇기) — 네모 · U 홈(가까운 쪽 먼저) · 밖을 향하면 없음
        {
            var cxS = BoundaryDraw.Prepare(sq, fl)!; var cxU = BoundaryDraw.Prepare(u, ufl)!;
            var r1 = BoundaryDraw.RayExit(cxS, 20, 30, 1, 0); var r2 = BoundaryDraw.RayExit(cxS, 20, 30, -3, 0); var r3 = BoundaryDraw.RayExit(cxS, 20, 30, 1, 1);
            var r4 = BoundaryDraw.RayExit(cxU, 15, 50, 1, 0);   // 홈(x 30) 먼저
            var r5 = BoundaryDraw.RayExit(cxS, 120, 50, 1, 0);  // 경계 밖에서 바깥으로 — 없음
            var r6 = BoundaryDraw.RayExit(cxS, 20, 30, 0, 0);   // 방향 없음
            bool ok22 = r1 is { } a1 && Math.Abs(a1.X - 100) < 1e-9 && Math.Abs(a1.Y - 30) < 1e-9 && r2 is { } a2 && Math.Abs(a2.X) < 1e-9 && Math.Abs(a2.Y - 30) < 1e-9
                && r3 is { } a3 && Math.Abs(a3.X - 90) < 1e-9 && Math.Abs(a3.Y - 100) < 1e-9 && r4 is { } a4 && Math.Abs(a4.X - 30) < 1e-9 && Math.Abs(a4.Y - 50) < 1e-9 && r5 == null && r6 == null;
            check("S208 ㉒ 반직선 경계 출구 — 네모 (100,30) · (0,30) · (90,100) · U 홈 (30,50) · 밖으로 = 없음 · 방향 없음 = 없음", ok22, $"{r1} · {r2} · {r3} · {r4} · {r5} · {r6}");
        }
    }
}

// ── S208b ★[v117.9.3 · JACK 1112 «안 되면 안 되는 사유를 직관적으로 — 초보 · 단답형»] [옹벽 추가] 거절 화면 글(WallEasy) — 습니다체 없음 · 할 일(→) 있음 · 숫자 옮김
static class WallEasyCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S208b 옹벽 추가 — 거절 화면 글(WallEasy) ==");
        static bool Terse(string s) => !s.Contains("니다") && !s.Contains("세요") && !s.Contains("어요") && !s.Contains("해요") && s.Contains("→");
        var a = WallEasy.Build("자리", "자리 모자람 — 소단이 바닥보다 0.784 m 높습니다(바닥까지 필요 1.06 m · 있는 거리 0.65 m) (1.0, 2.0) — 선을 바닥에서 더 떼거나 수직만(V)");
        check("S208b ① 자리(다단) — 필요 1.06 · 있음 0.65 · 수직만(V) · 단답", Terse(a) && a.Contains("필요 1.06 m") && a.Contains("있음 0.65 m") && a.Contains("수직만(V)"), a);
        var b = WallEasy.Build("자리", "자리 모자람 — 선에서 부지 바닥까지 0.60 m · 필요 2.20 m(2단 · 소단 1.0 m) (1.0, 2.0) — 선을 바깥으로 옮기거나 소단을 줄이세요");
        check("S208b ② 자리(옛 모양) — 필요 2.20 · 있음 0.60", Terse(b) && b.Contains("필요 2.20 m") && b.Contains("있음 0.60 m"), b);
        var c = WallEasy.Build("자리", "자리 모자람 — 소단 줄이 부지 바닥에 0.050 m까지 붙습니다(필요 ≥ 0.100 m · 2단 · 깊이 2.15 m) (1.0, 2.0) — 선을 바깥으로 옮기거나 소단을 줄이세요");
        check("S208b ③ 자리(소단 줄) — 필요 0.10 · 있음 0.05", Terse(c) && c.Contains("필요 0.10 m") && c.Contains("있음 0.05 m"), c);
        var d = WallEasy.Build("다단 합성", "소단 자리가 한 고리가 아닙니다(조각 2 · 구멍 0)");
        check("S208b ④ 안쪽 계산 실패 → «계산 실패(프로그램 문제)»", d == WallEasy.Bug && Terse(d), d);
        var e = WallEasy.Build("겹침", "다단 소단 자리가 옹벽 3과 겹칩니다(2.00㎡) — 수직만(V) 또는 선을 옮기세요");
        check("S208b ⑤ 겹침 — 상대 이름 · 되돌리기 · 수직만(V)", Terse(e) && e.Contains("(옹벽 3)") && e.Contains("[옹벽 삭제]") && e.Contains("수직만(V)"), e);
        var f = WallEasy.Build("다단 수직", "P0′에 대고 바깥 벽을 못 지었습니다 — 바닥: 벽 띠가 부지 바닥(계획선)에 닿습니다 (1.0, 2.0) — 높이 3.00 m 벽은 띠 폭 0.030 m가 필요합니다");
        check("S208b ⑥ 다단 수직 — 안쪽 까닭(바닥)을 풀어 씀", Terse(f) && f.Contains("부지 바닥"), f);
        var g = WallEasy.Cut(BoundaryCut.Code.Many, "경계를 4번 지납니다 — 정확히 두 번만 지나게 그리세요");
        check("S208b ⑦ 판정 — 경계 4번 지남", Terse(g) && g.Contains("4번"), g);
        var h = WallEasy.Cut(BoundaryCut.Code.Floor, "폴리선이 부지 바닥(계획선)에 0.05 m까지 붙습니다(0.1 m 안)");
        check("S208b ⑧ 판정 — 바닥에 0.05 m", Terse(h) && h.Contains("0.05 m"), h);
        var i = WallEasy.Cut(BoundaryCut.Code.Blocked, "옹벽 2과 겹칩니다(폴리선이 그 안을 지남)");
        check("S208b ⑨ 판정 — 겹침 상대 이름", Terse(i) && i.Contains("(옹벽 2)"), i);
        // 모든 판정 코드 · 모든 짓기 코드가 단답형(습니다체 없음 · 할 일 있음)
        bool all = true; string bad = "";
        foreach (BoundaryCut.Code k in Enum.GetValues(typeof(BoundaryCut.Code))) { if (k == BoundaryCut.Code.Ok) continue; var s = WallEasy.Cut(k, ""); if (!Terse(s)) { all = false; bad += $"{k}:{s} | "; } }
        foreach (var k in new[] { "자리", "섞인 선", "섞임 겹침", "겹침", "구간", "단 수", "도달", "바깥 벽", "바닥", "방향", "원지반", "정지면", "정지면 밖", "옛 사면", "끝 높이", "끝", "마구리", "바닥 높이 끊김", "턱", "계단", "단 표", "옛 소단", "소단 높이", "면 높이", "상자", "재료", "다단 수직", "다단 합성", "고리", "자가검증", "줄", "측선", "가로대", "띠" })
        { var s = WallEasy.Build(k, ""); if (!Terse(s)) { all = false; bad += $"{k}:{s} | "; } }
        check("S208b ⑩ 모든 코드 — 습니다체 없음 · 할 일(→) 있음", all, bad);
        // ⑪ ★[v117.15 · JACK 1112 «끝이 초록 경계 위인데 "선 끝이 초록 경계가 아님"»] «끝 높이» — 걸린 것 = 경계가 땅과 어긋난 양(cm) · 진입도로 바깥 테두리 = 면 오차 · 옛 거짓 글 없음 · 1 cm 한도는 그대로(글만)
        const string arcWhy = "잘라낼 자리 바깥 테두리(옛 경계)에서 정지면과 원지반이 0.032 m 다릅니다 (10.0, 20.0) — 옛 경계가 데이라잇이 아닌 자리(앞 옹벽 · 자른 자리) · 선을 옮기세요";
        var j1 = WallEasy.Build("끝 높이", arcWhy, road: true);
        var j2 = WallEasy.Build("끝 높이", "A에서 정지면과 원지반이 0.250 m 다릅니다 — 끝이 데이라잇이 아닙니다 · 마구리는 다음 판 (1.0, 2.0)", road: true);
        var j3 = WallEasy.Build("끝 높이", "띠 끝 X_B에서 정지면이 원지반 반대쪽으로 1.500 m(1 cm 넘음) — 끝 B 곁 옛 경계가 데이라잇이 아닙니다 (1.00, 2.00) · 선 끝을 옮기세요");
        var j4 = WallEasy.Build("다단 수직", "P0′에 대고 바깥 벽을 못 지었습니다 — 끝 높이: " + arcWhy, road: true);
        var j5 = WallEasy.Build("끝 높이", arcWhy);   // 부지 — 진입도로 까닭 안 붙음
        check("S208b ⑪ 끝 높이 — 경계가 땅과 3.2 cm 어긋남(진입도로 면 오차) · 끝 25 cm · 띠 끝 1.50 m · 다단 수직 풀어 씀 · 부지는 까닭 없음 · «경계선 위로» 없음",
              new[] { j1, j2, j3, j4, j5 }.All(q => Terse(q) && !q.Contains("경계선 위로")) && j1.Contains("땅과 3.2 cm 어긋남") && j1.Contains("진입도로 면 오차")
              && j2.Contains("25 cm") && !j2.Contains("면 오차") && j3.Contains("1.50 m") && j4 == j1 && j5.Contains("3.2 cm") && !j5.Contains("진입도로"),
              $"{j1} | {j2} | {j3} | {j4} | {j5}");
    }
}
