// ── S173 ★★★[v105.3 · 계획_v105_3.md · JACK 1026 «가로지르는 벽만 (노란 선)»] 횡단 2D 터파기선 · 배면 · 구조물 공제 · 3D 조각 원뿔 낮추기 ──
//   평지 벽 하나(손셈 넓이 · 기초 밑 = 바닥) · 비스듬(늘임) · 소단(꺾임) · 마주 보는 두 벽(멀면 따로 · 가까우면 낮은 쪽) · 쌓인 세 단(아래 단이 이김)
//   · 나란한 벽(빠짐) · 도랑(뒤 수직) · 배면(직선 → 평탄 · 소단에 닿으면 끝까지 · 설계 사면 없음) · 꺾임점(표본 간격과 무관한 넓이) · ConeLower
//   만든 꼴(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;

static class Xsec2DCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S173 횡단 2D 터파기선(가로지르는 벽만) · 배면 · 공제 · 3D 원뿔 낮추기 ==");
        var t = WallSection.DefaultTable(WallSection.Kind.역T형);
        int row = t.Rows.FindIndex(r => Math.Abs(r[0] - 6.0) < 1e-9);
        var c = new WallSection.Common(0.10, 0.10, 0.02, 0.01);
        const double MinSin = 0.25, Margin = 0.5;
        var sp = new WallExcav.SlopeSpec(1.2, false, 3, 1);
        var spB = new WallExcav.SlopeSpec(1.2, true, 3, 1);
        // 곧은 벽 줄(x 0 → 20 · y = y0 · 앞 땅 toe · 전체 높이 hTot · 근입 1)
        WallExcav.RecLine Line(double y0, int soil, double toe, double hTot = 6.0, bool up = true, WallSection.Kind k = WallSection.Kind.역T형, int rw = -1) => new()
        {
            Kind = k, Soil = soil, Up = up, SlopeW = 0.01,
            Pts = new() { new(0, y0, toe, hTot, 1.0, rw < 0 ? row : rw), new(10, y0, toe, hTot, 1.0, rw < 0 ? row : rw), new(20, y0, toe, hTot, 1.0, rw < 0 ? row : rw) },
        };
        WallExcav.SecRecord Rec(params WallExcav.RecLine[] lines)
        {
            var rec = new WallExcav.SecRecord { Margin = Margin, Common = c };
            rec.Tables[WallSection.Kind.역T형] = t;
            foreach (var l in lines) { if (!rec.Tables.ContainsKey(l.Kind)) rec.Tables[l.Kind] = WallSection.DefaultTable(l.Kind); rec.Lines.Add(l); }
            return rec;
        }
        var sh = WallSection.BuildRow(WallSection.Kind.역T형, t.Rows[row], 6.0, c with { GradeFace = 0.01 }, true, 1.0);
        double zf = 100 + sh.ZBottom, xF = sh.XFront - Margin, xB = sh.XBack + Margin;
        // 넓이(땅 − E) — 꺾임점 + 고른 간격(step)
        double Area(WallExcav.Xs2D X, double step, double lo, double hi, params double[] extra)
        {
            var xs = X.Breaks.Concat(extra).ToList();
            for (double a = lo; a <= hi + 1e-9; a += step) xs.Add(a);
            xs = xs.Where(a => a >= lo - 1e-12 && a <= hi + 1e-12).Distinct().OrderBy(a => a).ToList();
            double s = 0;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double a0 = xs[i], a1 = xs[i + 1], e0 = X.E(a0), e1 = X.E(a1);
                if (double.IsNaN(e0) || double.IsNaN(e1)) continue;
                s += ((X.Lz(a0) - e0) + (X.Lz(a1) - e1)) / 2 * (a1 - a0);
            }
            return s;
        }
        bool Has(List<double> v, double x, double tol = 1e-6) => v.Any(q => Math.Abs(q - x) < tol);

        // ① 평지 벽 하나 — 자르는 선 x 5 · y −30 → 30(축 a = y) · 앞 100 · 뒤 105(앞면 선에서 계단 — 땅 함수)
        Func<double, double> G1 = a => a <= 0 ? 100 : 105;
        var X1 = WallExcav.Excav2D(Rec(Line(0, +1, 100)), 5, -30, 5, 30, 30, 30, G1, G1, G1, sp, null, MinSin);
        double dLo = xF - 1.1 * 1.2, dHi = xB + 6.1 * 1.2;
        var p1 = X1.Profs.Count == 1 ? X1.Profs[0] : null;
        check("S173 ① 벽 하나 — 바닥 = 앞 땅 + 단면 바닥(버림 밑) · 바닥 끝 = 단면 끝 ± 여유 · 데이라잇 손셈(앞 1.1 × 1.2 · 뒤 6.1 × 1.2)",
            p1 != null && Math.Abs(p1.Zf - zf) < 1e-9 && Math.Abs(p1.FLo - xF) < 1e-9 && Math.Abs(p1.FHi - xB) < 1e-9 && Math.Abs(p1.DayLo - dLo) < 1e-6 && Math.Abs(p1.DayHi - dHi) < 1e-6,
            p1 == null ? $"벽 {X1.Profs.Count}" : $"바닥 {p1.Zf:F3}/{zf:F3} · {p1.FLo:F3}~{p1.FHi:F3} · 데이라잇 {p1.DayLo:F4}/{dLo:F4} · {p1.DayHi:F4}/{dHi:F4}");
        double e0 = X1.E(0), eMid = X1.E(xB + 3.0), eOut = X1.E(dHi + 0.1);
        check("S173 ① 기초 밑 = 그린 기초 바닥(기울지 않음) · 뒤 법면 1:1.2 · 데이라잇 밖 없음",
            Math.Abs(e0 - zf) < 1e-12 && Math.Abs(X1.E(sh.XFront + 0.01) - zf) < 1e-12 && Math.Abs(X1.E(sh.XBack - 0.01) - zf) < 1e-12 && Math.Abs(eMid - (zf + 3.0 / 1.2)) < 1e-9 && double.IsNaN(eOut),
            $"{e0:F4} · {eMid:F4} · {eOut}");
        double area1 = Area(X1, 0.1, -30, 30, -1e-9, 1e-9);
        double hand1 = 0.5 * 1.32 * 1.1 + 1.1 * (0 - xF) + 6.1 * xB + 0.5 * 7.32 * 6.1;
        check("S173 ① 넓이 = 손셈(앞 세모 + 바닥 두 칸 + 뒤 세모)", Math.Abs(area1 - hand1) < 1e-6, $"{area1:F6} / {hand1:F6}");
        check("S173 ① 구간 하나 · 꺾임점에 바닥 끝 · 데이라잇", X1.Spans.Count == 1 && Has(X1.Breaks, xF) && Has(X1.Breaks, xB) && Has(X1.Breaks, dLo, 1e-5) && Has(X1.Breaks, dHi, 1e-5),
            $"구간 {X1.Spans.Count} · 꺾임 {X1.Breaks.Count}");

        // ② 비스듬(60°) — 늘임 1/sin60 · 데이라잇 거리 × 늘임
        double th = 60 * Math.PI / 180, cx = 5, cy = 0;
        double lx2 = cx - 30 * Math.Cos(th), ly2 = cy - 30 * Math.Sin(th), rx2 = cx + 30 * Math.Cos(th), ry2 = cy + 30 * Math.Sin(th);
        var X2 = WallExcav.Excav2D(Rec(Line(0, +1, 100)), lx2, ly2, rx2, ry2, 30, 30, G1, G1, G1, sp, null, MinSin);
        double st2 = 1 / Math.Sin(th);
        var p2 = X2.Profs.Count == 1 ? X2.Profs[0] : null;
        check("S173 ② 60° — 바닥 · 법면이 각도대로 늘어남(바닥 끝 xB × 1/sin · 데이라잇 7.32 × 1/sin)",
            p2 != null && Math.Abs(p2.C.Stretch - st2) < 1e-9 && Math.Abs(p2.FHi - xB * st2) < 1e-9 && Math.Abs(p2.DayHi - (xB + 7.32) * st2) < 1e-6 && Math.Abs(X2.E(0) - zf) < 1e-12,
            p2 == null ? "없음" : $"{p2.C.Stretch:F4} · {p2.FHi:F4}/{xB * st2:F4} · {p2.DayHi:F4}/{(xB + 7.32) * st2:F4}");

        // ③ 소단(3 m마다 1 m) — 뒤 6.1 m 오름: 3.6 · 1 · 3.6 · 1 · 0.12
        var X3 = WallExcav.Excav2D(Rec(Line(0, +1, 100)), 5, -30, 5, 30, 30, 30, G1, G1, G1, spB, null, MinSin);
        var p3 = X3.Profs[0];
        double d3 = xB + 3.6 + 1 + 3.6 + 1 + 0.12;
        check("S173 ③ 소단 — 데이라잇 손셈 · 소단 모서리 넷이 꺾임점 · 소단 위 높이 = 바닥 + 3",
            Math.Abs(p3.DayHi - d3) < 1e-6 && Has(X3.Breaks, xB + 3.6) && Has(X3.Breaks, xB + 4.6) && Has(X3.Breaks, xB + 8.2) && Has(X3.Breaks, xB + 9.2) && Math.Abs(X3.E(xB + 4.0) - (zf + 3)) < 1e-9,
            $"{p3.DayHi:F4}/{d3:F4} · 소단 위 {X3.E(xB + 4.0):F4}");

        // ④ 마주 보는 두 벽 — 30 m 떨어짐: 두 선이 따로(가운데 땅) · 9 m: 만나 한 구간(가운데 = 낮은 쪽)
        Func<double, double> G4(double w) => a => a <= 0 || a >= w ? 100 : 105;
        var X4 = WallExcav.Excav2D(Rec(Line(0, +1, 100), Line(30, -1, 100)), 5, -20, 5, 50, 20, 50, G4(30), G4(30), G4(30), sp, null, MinSin);
        check("S173 ④ 30 m 마주 봄 — 구간 둘(두 옹벽선이 안 만남) · 가운데 터파기 없음", X4.Profs.Count == 2 && X4.Spans.Count == 2 && double.IsNaN(X4.E(15)),
            $"벽 {X4.Profs.Count} · 구간 {X4.Spans.Count} · 가운데 {X4.E(15)}");
        var X4b = WallExcav.Excav2D(Rec(Line(0, +1, 100), Line(9, -1, 100)), 5, -20, 5, 50, 20, 50, G4(9), G4(9), G4(9), sp, null, MinSin);
        double mid4 = 4.5, want4 = Math.Min(X4b.Profs[0].Z(mid4), X4b.Profs[1].Z(mid4));
        check("S173 ④ 9 m 마주 봄 — 한 구간 · 가운데 = 두 선 중 낮은 쪽", X4b.Spans.Count == 1 && Math.Abs(X4b.E(mid4) - want4) < 1e-12, $"구간 {X4b.Spans.Count} · {X4b.E(mid4):F4}/{want4:F4}");

        // ⑤ 쌓인 세 단(1.05 m 물러남 · 앞 땅 100 · 105 · 110) · 뒤 언덕 118 — 아래 단 선이 이김 · 위 단 바닥은 E 위(되메우기 위에 앉음)
        Func<double, double> G5 = a => a <= 0 ? 100 : 118;
        var X5 = WallExcav.Excav2D(Rec(Line(0, +1, 100), Line(1.05, +1, 105), Line(2.1, +1, 110)), 5, -30, 5, 40, 30, 40, G5, G5, G5, sp, null, MinSin);
        double upFloor = 105 + sh.ZBottom;
        check("S173 ⑤ 쌓인 세 단 — 셋 그림 · 위 단 앞면 선 자리 E = 아래 단 바닥 · 위 단 바닥보다 낮음 · 데이라잇 = 아래 단 법면",
            X5.Profs.Count == 3 && Math.Abs(X5.E(1.05) - zf) < 1e-12 && X5.E(1.05) < upFloor - 1 && X5.Spans.Count == 1 && Math.Abs(X5.Spans[0].Hi - (xB + 19.1 * 1.2)) < 1e-6,
            $"벽 {X5.Profs.Count} · E {X5.E(1.05):F3} · 위 단 바닥 {upFloor:F3} · 끝 {X5.Spans[0].Hi:F4}/{xB + 19.1 * 1.2:F4}");

        // ⑥ 나란한 벽(10°) — 빠짐 · 터파기 없음
        double a10 = 10 * Math.PI / 180;
        var X6 = WallExcav.Excav2D(Rec(Line(0, +1, 100)), 5 - 30 * Math.Cos(a10), -30 * Math.Sin(a10), 5 + 30 * Math.Cos(a10), 30 * Math.Sin(a10), 30, 30, G1, G1, G1, sp, null, MinSin);
        check("S173 ⑥ 나란한 벽(sin 0.17 < 0.25) — 빠짐 1 · 그린 벽 0 · 터파기 없음", X6.Skipped == 1 && X6.Profs.Count == 0 && X6.Spans.Count == 0, $"빠짐 {X6.Skipped} · 벽 {X6.Profs.Count} · 구간 {X6.Spans.Count}");

        // ⑦ 도랑(앵커식 패널) — 뒤(흙 쪽) 법면 없음 · 앞면 선에서 수직
        var tp = WallSection.DefaultTable(WallSection.Kind.패널식);
        var X7 = WallExcav.Excav2D(Rec(Line(0, +1, 100, tp.Rows[0][0] + 1.0, true, WallSection.Kind.패널식, 0)), 5, -30, 5, 30, 30, 30, G1, G1, G1, sp, null, MinSin);
        var p7 = X7.Profs.Count == 1 ? X7.Profs[0] : null;
        check("S173 ⑦ 도랑 — 뒤 닫힘 · 바닥 뒤 끝 = 앞면 선 · 뒤 0.1 mm 밖 터파기 없음 · 꺾임점 양쪽 0.1 mm",
            p7 != null && !p7.OpenHi && p7.OpenLo && Math.Abs(p7.FHi) < 1e-12 && !double.IsNaN(X7.E(-0.001)) && double.IsNaN(X7.E(0.001)) && Has(X7.Breaks, -1e-4) && Has(X7.Breaks, 1e-4),
            p7 == null ? "없음" : $"뒤 {p7.OpenHi} · 바닥 끝 {p7.FHi:E2} · {X7.E(-0.001):F3} / {X7.E(0.001)}");

        // ⑧ 배면 — (a) 뒤 땅 107(벽 꼭대기 103 · hTot 4) · 1:1.5 직선 → 평탄 103
        Func<double, double> G8 = a => a <= 0 ? 100 : 107;
        var r8 = Rec(Line(0, +1, 100, 4.0));
        var X8 = WallExcav.Excav2D(r8, 5, -30, 5, 30, 30, 30, G8, G8, G8, sp, 1.5, MinSin);
        var sh4 = WallSection.BuildRow(WallSection.Kind.역T형, t.Rows[row], 4.0, c with { GradeFace = 0.01 }, true, 1.0);
        double e8 = sh4.XBack + Margin + (107 - (100 + sh4.ZBottom)) * 1.2;
        var bk8 = X8.Backs.Count == 1 ? X8.Backs[0] : null;
        double b8a = X8.B(e8 - 1, out bool i8a, out _), b8b = X8.B(e8 - 6.5, out bool i8b, out _), b8f = X8.B(-5, out bool i8f, out _);
        check("S173 ⑧a 배면 — 데이라잇에서 1:1.5 직선(107 − 1/1.5) · 6 m 넘으면 꼭대기 103 평탄 · 앞쪽 없음 · 닿음 없음",
            bk8 != null && Math.Abs(bk8.Day - e8) < 1e-6 && Math.Abs(bk8.T - 103) < 1e-9 && double.IsNaN(bk8.Contact) && i8a && Math.Abs(b8a - (107 - 1 / 1.5)) < 1e-6 && i8b && Math.Abs(b8b - 103) < 1e-9 && !i8f && double.IsNaN(b8f),
            bk8 == null ? $"배면 {X8.Backs.Count}" : $"e {bk8.Day:F4}/{e8:F4} · T {bk8.T:F3} · 닿음 {bk8.Contact} · {b8a:F4}/{i8a} · {b8b:R}/{i8b} · 앞 {b8f}/{i8f}");
        // (b) 소단에 닿음 — 끝까지 터파기선(소단 · 법면) · 직선이 다시 위로 나와도 안 따름
        var X8b = WallExcav.Excav2D(r8, 5, -30, 5, 30, 30, 30, G8, G8, G8, spB, 1.5, MinSin);
        var bkb = X8b.Backs.Count == 1 ? X8b.Backs[0] : null;
        double zfb = 100 + sh4.ZBottom, rise = 107 - zfb;   // 8.1 — 3 · 소단 · 3 · 소단 · 2.1
        double eb = sh4.XBack + Margin + 3.6 + 1 + 3.6 + 1 + 2.1 * 1.2;
        double sC = (107 - (zfb + 6 - 0.01)) * 1.5;           // 위 소단(zfb + 6)에서 1 cm 넘게 아래로 가는 자리
        double s55 = 5.5, bAt = X8b.B(eb - s55, out bool iAt, out _), eAt = X8b.E(eb - s55), dAt = 107 - s55 / 1.5;
        check("S173 ⑧b 배면 — 위 소단에 닿음(손셈 자리) · 그 뒤 직선이 터파기선 위로 나와도 터파기선을 따름(v105.2 «끝까지»)",
            bkb != null && Math.Abs(rise - 8.1) < 1e-9 && !double.IsNaN(bkb.Contact) && Math.Abs(Math.Abs(bkb.Contact - eb) - sC) < 1e-5 && iAt && dAt > eAt + 0.05 && Math.Abs(bAt - Math.Max(eAt, 103)) < 1e-9,
            bkb == null ? $"배면 {X8b.Backs.Count}" : $"닿음 {Math.Abs(bkb.Contact - eb):F4}/{sC:F4} · s 5.5: B {bAt:F4} · E {eAt:F4} · D {dAt:F4}");
        // (c) 설계 사면(정지면_DH ≠ 원지반 2 cm — 그린 계획면은 같아도) — 배면 없음 · (d) 쌓인 단 — 데이라잇 쪽 첫 벽 = 맨 위 단 · T = 맨 위 단 꼭대기
        Func<double, double> P8 = a => a > 3 && a < 6 ? G8(a) - 0.02 : G8(a);
        var X8c = WallExcav.Excav2D(r8, 5, -30, 5, 30, 30, 30, G8, G8, P8, sp, 1.5, MinSin);
        X8c.B(4.5, out bool i8c, out _);
        var X8d = WallExcav.Excav2D(Rec(Line(0, +1, 100), Line(1.05, +1, 105), Line(2.1, +1, 110)), 5, -30, 5, 40, 30, 40, a => a <= 0 ? 100 : 118, a => a <= 0 ? 100 : 118, a => a <= 0 ? 100 : 118, sp, 1.5, MinSin);
        check("S173 ⑧c 설계 사면 자리는 배면 없음 · ⑧d 쌓인 단 — 배면 하나 · 벽 = 맨 위 단 앞면 선 · T = 115",
            !i8c && X8d.Backs.Count == 1 && Math.Abs(X8d.Backs[0].Wall - 2.1) < 1e-9 && Math.Abs(X8d.Backs[0].T - 115) < 1e-9,
            $"{i8c} · 배면 {X8d.Backs.Count} · {(X8d.Backs.Count > 0 ? $"{X8d.Backs[0].Wall:F2}/{X8d.Backs[0].T:F2}" : "")}");

        // (e) 등 맞댄 두 절토 벽(흙 쪽이 서로 마주 봄 · 사이 언덕) — 사이에 데이라잇 없음 → 배면 없음 · 등 돌린 두 벽(사이 길) — 배면 둘 · 안 겹침
        Func<double, double> Gbb = a => a <= 0 || a >= 10 ? 100 : 107;
        var X8e = WallExcav.Excav2D(Rec(Line(0, +1, 100, 4.0), Line(10, -1, 100, 4.0)), 5, -30, 5, 40, 30, 40, Gbb, Gbb, Gbb, sp, 1.5, MinSin);
        Func<double, double> Gfa = a => a >= 0 && a <= 10 ? 100 : 107;
        var X8f = WallExcav.Excav2D(Rec(Line(0, -1, 100, 4.0), Line(10, +1, 100, 4.0)), 5, -30, 5, 40, 30, 40, Gfa, Gfa, Gfa, sp, 1.5, MinSin);
        check("S173 ⑧e 등 맞댄 두 벽 — 배면 없음 · 등 돌린 두 벽 — 배면 둘(각 벽 흙 쪽) · 안 겹침",
            X8e.Backs.Count == 0 && X8f.Backs.Count == 2 && X8f.Backs.OrderBy(q => q.Lo).First().Hi <= X8f.Backs.OrderBy(q => q.Lo).Last().Lo + 1e-9
            && X8f.Backs.Any(q => Math.Abs(q.Wall) < 1e-9 && q.Day < 0) && X8f.Backs.Any(q => Math.Abs(q.Wall - 10) < 1e-9 && q.Day > 10),
            $"맞댐 {X8e.Backs.Count} · 돌림 {X8f.Backs.Count} " + string.Join(" ", X8f.Backs.Select(q => $"[{q.Lo:F2}~{q.Hi:F2}]")));

        // ★[설치 전 검토 높음 1 · 중간 3] 그림 꼭짓점 = 꺾임점 — 배면마다 데이라잇 곁(1e-5 안) 벽 쪽 꺾임점에서 배면이 «안»이어야(흙 쪽이 왼쪽인 벽은 1:n 직선 6 m가 빠졌다)
        //   · 수량과 같은 합치기(1e-6 · 앞 점 남김)로 표본 0.1 m · 0.013 m 넓이 = 손셈(평지 벽 하나 · 왼쪽 데이라잇 첫 칸 빠짐 없음)
        bool BackStartsAtDay(WallExcav.Xs2D X)
            => X.Backs.All(bk => X.Breaks.Any(b => Math.Abs(b - bk.Day) <= 1e-5 && Math.Sign(b - bk.Day) == Math.Sign(bk.Wall - bk.Day) && !double.IsNaN(X.B(b, out bool ib, out _)) && ib));
        var X8g = WallExcav.Excav2D(Rec(Line(0, -1, 100, 4.0)), 5, -30, 5, 30, 30, 30, a => a >= 0 ? 100 : 107, a => a >= 0 ? 100 : 107, a => a >= 0 ? 100 : 107, sp, 1.5, MinSin);
        double QArea(WallExcav.Xs2D X, double step, double lo, double hi, params double[] extra)
        {
            var raw = X.Breaks.Concat(extra).ToList();
            for (double a = lo; a <= hi + 1e-9; a += step) raw.Add(a);
            raw = raw.Where(a => a >= lo - 1e-12 && a <= hi + 1e-12).OrderBy(a => a).ToList();
            var xs = new List<double> { raw[0] };
            for (int k = 1; k < raw.Count; k++) if (raw[k] - xs[^1] > 1e-6) xs.Add(raw[k]);   // QtyAt 합치기와 같은 법
            double s = 0;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                double e0 = X.E(xs[i]), e1 = X.E(xs[i + 1]);
                if (double.IsNaN(e0) || double.IsNaN(e1)) continue;
                s += ((X.Lz(xs[i]) - e0) + (X.Lz(xs[i + 1]) - e1)) / 2 * (xs[i + 1] - xs[i]);
            }
            return s;
        }
        double q1 = QArea(X1, 0.1, -30, 30, -2e-6, 2e-6), q2 = QArea(X1, 0.013, -30, 30, -2e-6, 2e-6);   // 시험 땅(앞면 선 계단)의 양쪽 — 합치기(1e-6)보다 넓게
        check("S173 ⑧g 배면 그림 첫 꼭짓점 = 데이라잇(흙 쪽 왼쪽 · 오른쪽 · 등 돌린 두 벽) · 수량 합치기로 넓이 = 손셈(0.1 · 0.013 m)",
            X8g.Backs.Count == 1 && BackStartsAtDay(X8g) && BackStartsAtDay(X8) && BackStartsAtDay(X8f) && Math.Abs(q1 - hand1) < 1e-4 && Math.Abs(q2 - hand1) < 1e-4,
            $"왼쪽 {BackStartsAtDay(X8g)} · 오른쪽 {BackStartsAtDay(X8)} · 둘 {BackStartsAtDay(X8f)} · 넓이 {q1:F6}/{q2:F6}/{hand1:F6}");

        // ⑨ 꺾임점이 들어가면 넓이가 고른 간격과 무관(소단 · 두 벽)
        double aA = Area(X3, 0.1, -30, 30, -1e-9, 1e-9), aB = Area(X3, 0.013, -30, 30, -1e-9, 1e-9);
        double aC = Area(X4b, 0.1, -20, 50, -1e-9, 1e-9, 9 - 1e-9, 9 + 1e-9), aD = Area(X4b, 0.013, -20, 50, -1e-9, 1e-9, 9 - 1e-9, 9 + 1e-9);
        check("S173 ⑨ 꺾임점 — 표본 0.1 m · 0.013 m 넓이 같음(소단 · 마주 본 두 벽)", Math.Abs(aA - aB) < 1e-6 && Math.Abs(aC - aD) < 1e-6, $"{aA:F6}/{aB:F6} · {aC:F6}/{aD:F6}");

        // ⑩ 구조물 공제 — 그린 벽 단면만(나란한 벽은 0) · 단면 넓이와 같음(바닥 ~ 높이 안 · 직각)
        var xsI = Enumerable.Range(0, 601).Select(i => -30 + i * 0.1).ToArray();
        var loI = xsI.Select(a => 90.0).ToArray(); var hiI = xsI.Select(a => 120.0).ToArray();
        var cs1 = X1.Integrate(xsI, loI, hiI);
        var cs6 = X6.Integrate(xsI, loI, hiI);
        var cs2 = X2.Integrate(xsI, loI, hiI);
        check("S173 ⑩ 공제 — 직각 벽 하나 = 단면 구조물 넓이(±0.1%) · 60° = 그린 대로 늘인 넓이(× 1/sin) · 나란해 뺀 벽 = 0",
            Math.Abs(cs1.Struct - sh.StructArea) <= sh.StructArea * 1e-3 && Math.Abs(cs2.Struct - sh.StructArea * st2) <= sh.StructArea * st2 * 1e-3 && cs6.Struct == 0,
            $"{cs1.Struct:F4}/{sh.StructArea:F4} · 60° {cs2.Struct:F4}/{sh.StructArea * st2:F4} · 나란 {cs6.Struct}");

        // ⑫ 빠진 벽 알림 — 횡단(남북 x 5 · 10 · 15)과 나란한 벽(x 0 → 0 · y 0 → 20)은 길이 20 · 직각 벽(y 0)은 0 · 띠 밖은 안 셈
        var recU = Rec(Line(0, +1, 100));
        recU.Lines.Add(new WallExcav.RecLine { Kind = WallSection.Kind.역T형, Soil = +1, Up = true, SlopeW = 0.01, Pts = new() { new(8, 0, 100, 6.0, 1.0, row), new(8, 10, 100, 6.0, 1.0, row), new(8, 20, 100, 6.0, 1.0, row) } });
        var cutsU = new List<(double, double, double, double)> { (5, -30, 5, 30), (10, -30, 10, 30), (15, -30, 15, 30) };
        double unc = WallExcav.UncutLength(recU, cutsU, MinSin, (x, y) => x >= 5 && x <= 15, out int ucs);
        double uncOut = WallExcav.UncutLength(recU, cutsU, MinSin, (x, y) => x > 100, out _);
        check("S173 ⑫ 빠진 벽 — 횡단과 나란한 벽 20 m(변 2) · 직각 벽 0 · 띠 밖 0", Math.Abs(unc - 20) < 1e-9 && ucs == 2 && uncOut == 0, $"{unc:F3} · 변 {ucs} · 띠 밖 {uncOut}");

        // ⑪ ConeLower — 평평한 바닥(바뀌지 않음) · 기운 바닥 끝 곁(손셈 원뿔로 낮춤) · 띠 안 · 위 그대로
        List<Point3> RingRect(Func<double, double> z) => new() { new(0, 0, z(0)), new(10, 0, z(10)), new(10, 2, z(10)), new(0, 2, z(0)) };
        Tri T3(double ax, double ay, double az, double bx, double by, double bz, double qx, double qy, double qz) => new(new Point3(ax, ay, az), new Point3(bx, by, bz), new Point3(qx, qy, qz));
        var flat = RingRect(_ => 100);
        var trF = new List<Tri> { T3(10, 0, 100, 13, 0, 100 + 3 / 1.2, 13, 2, 100 + 3 / 1.2), T3(5, 1, 100, 10, 0, 100, 10, 2, 100) };
        var outF = WallExcav.ConeLower(trF, flat, sp, out int lowF, out _);
        // 기운 바닥 z = 100 + 0.8x(1:1.25 — 법면보다 완만) · (12, 4)의 Build 값 = 가장 가까운 (10, 2) + h(√8)
        Func<double, double> zs = x => 100 + 0.8 * x;
        var slp = RingRect(zs);
        double zBuild = zs(10) + Math.Sqrt(8) / 1.2;
        double want = double.PositiveInfinity;
        foreach (var (qa, qb) in new[] { ((0.0, 0.0), (10.0, 0.0)), ((10.0, 0.0), (10.0, 2.0)), ((10.0, 2.0), (0.0, 2.0)), ((0.0, 2.0), (0.0, 0.0)) })
            for (int k = 0; k <= 20000; k++)
            {
                double u = k / 20000.0, fx = qa.Item1 + (qb.Item1 - qa.Item1) * u, fy = qa.Item2 + (qb.Item2 - qa.Item2) * u;
                want = Math.Min(want, zs(fx) + Math.Sqrt((fx - 12) * (fx - 12) + (fy - 4) * (fy - 4)) / 1.2);
            }
        var trS = new List<Tri> { T3(12, 4, zBuild, 10, 2, zs(10), 13, 2, zs(10) + 3 / 1.2), T3(5, 1, zs(5), 10, 2, zs(10), 0, 2, zs(0)) };
        var outS = WallExcav.ConeLower(trS, slp, sp, out int lowS, out double dropS);
        double got = outS[0].A.Z;
        check("S173 ⑪ ConeLower — 평평한 띠 그대로(낮춘 점 0) · 기운 띠 끝 곁 = 손셈 원뿔(±2 mm · 표본 0.05 m) · 띠 안 · 위 점 그대로",
            lowF == 0 && outF.SequenceEqual(trF) && lowS >= 1 && Math.Abs(got - want) < 2e-3 && got < zBuild - 0.1 && outS[1].Equals(trS[1]) && outS[0].B.Z == zs(10),
            $"평평 {lowF} · 기운 {got:F4}/{want:F4}(Build {zBuild:F4} · 낮춤 {dropS:F3}) · 낮춘 점 {lowS}");
        // ⑪b 계단 기초(0.5 m에 2 m 오름 — 1/m보다 가파름) — 높은 칸 곁 띠 밖 점은 가장 가까운 바닥(102) 아래로 안 내려감(낮은 칸 원뿔 101.68에서 멈춤)
        var stepR = new List<Point3> { new(0, 0, 100), new(5, 0, 100), new(5.5, 0, 102), new(10, 0, 102), new(10, 2, 102), new(5.5, 2, 102), new(5, 2, 100), new(0, 2, 100) };
        var trSt = new List<Tri> { T3(7, 2.3, 102 + 0.3 / 1.2, 6, 2, 102, 8, 2, 102) };
        var outSt = WallExcav.ConeLower(trSt, stepR, sp, out _, out _);
        check("S173 ⑪b 계단 기초 — 띠 곁 점 = 가장 가까운 바닥 102(낮은 칸 원뿔 101.68로 안 떨어짐 · 세로 면 없음)", Math.Abs(outSt[0].A.Z - 102) < 1e-9, $"{outSt[0].A.Z:F4}");
    }
}
