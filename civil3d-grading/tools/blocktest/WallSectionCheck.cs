// ── S168 ★★★[v104 · 계획_v104.md · JACK 1021 «옹벽 형태별 일반단면도 · 매개변수로 단면 모양 조종»] 옹벽 형식별 일반단면(WallSection) ──
//   첫 표(공공 자료) 검사 · 단면 넓이를 표로 손셈 · 표 고르기(올림 · 첫 줄 아래 · 표 밖) · 보강토 규칙 · 터파기 범위 · 표 기록 왕복
//   만든 꼴 · 공공 표 값만(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class WallSectionCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S168 옹벽 형식별 일반단면 — 첫 표 · 단면 넓이 · 표 고르기 · 터파기 범위 · 기록 ==");
        var c = new WallSection.Common();

        // ① 첫 표가 모두 쓸 수 있는 표인가 · 역T형 · L형 · 반중력식은 B = B1 + t_bot + B2가 표준도 값과 같은가
        foreach (var info in WallSection.Kinds)
        {
            var t = WallSection.DefaultTable(info.Id);
            check($"S168 ① 첫 표 {info.Name} — 쓸 수 있는 표(줄 {t.Rows.Count})", WallSection.Valid(t, out string why), why);
        }
        double[] wantT = { 2.00, 2.30, 2.60, 2.90, 3.20, 3.50, 3.80, 4.10, 4.40, 4.70, 5.00 };
        var tT = WallSection.DefaultTable(WallSection.Kind.역T형);
        bool okB = tT.Rows.Count == wantT.Length;
        for (int i = 0; okB && i < wantT.Length; i++) okB = Math.Abs(tT.Rows[i][3] + tT.Rows[i][2] + tT.Rows[i][4] - wantT[i]) < 1e-9 && Math.Abs(wantT[i] - (0.6 * tT.Rows[i][0] + 0.2)) < 1e-9;
        check("S168 ① 역T형 B = B1 + t_bot + B2 = 표준도 T-1~T-11 = 0.6H + 0.2", okB, "");
        double[] wantL = { 2.00, 2.30, 2.60, 2.90, 3.20, 3.50, 3.80, 4.10, 4.40, 5.00, 5.60 };
        var tL = WallSection.DefaultTable(WallSection.Kind.L형);
        bool okL = tL.Rows.Count == wantL.Length;
        for (int i = 0; okL && i < wantL.Length; i++) okL = tL.Rows[i][3] == 0 && Math.Abs(tL.Rows[i][2] + tL.Rows[i][4] - wantL[i]) < 1e-9;
        check("S168 ① L형 앞굽 0 · B = t_bot + B2 = 표준도 L-1~L-11", okL, "");

        // ② 단면 넓이 — 손셈
        //   역T형 H=5(T-5): 저판 3.2 × 0.5 + 벽 (0.5 + 0.3)/2 × 4.5 + 버림 (3.2 + 0.2) × 0.1 = 1.6 + 1.8 + 0.34 = 3.74
        var sT5 = WallSection.BuildRow(WallSection.Kind.역T형, tT.Rows[4], 5.0, c, cut: true);
        check("S168 ② 역T형 H 5 단면 넓이 3.74㎡(저판 1.6 + 벽 1.8 + 버림 0.34)", Math.Abs(sT5.StructArea - 3.74) < 1e-9, $"{sT5.StructArea:F6}");
        //   중력식 H=4(g-6): 콘크리트 (0.4 + 2.8)/2 × 4 = 6.40(표준도 재료표) + 버림 (2.8 + 0.2) × 0.1 = 0.30
        var tG = WallSection.DefaultTable(WallSection.Kind.중력식);
        var sG = WallSection.BuildRow(WallSection.Kind.중력식, tG.Rows[^1], 4.0, c, cut: true);
        double conc = sG.Parts.Where(p => p.Material == "콘크리트").Sum(p => WallSection.Area(p.Ring));
        check("S168 ② 중력식 H 4 콘크리트 6.40㎡(표준도 g-6 재료표) · 버림 0.30", Math.Abs(conc - 6.40) < 1e-9 && Math.Abs(sG.StructArea - 6.70) < 1e-9, $"콘크리트 {conc:F6} · 합 {sG.StructArea:F6}");
        //   L형 H=3(L-1): 앞굽 0 — 같은 점이 겹치지 않는다(조각마다 점이 다 다르다)
        var sL1 = WallSection.BuildRow(WallSection.Kind.L형, tL.Rows[0], 3.0, c, cut: true);
        bool noDup = sL1.Parts.All(p => p.Ring.Select(q => (Math.Round(q.X, 9), Math.Round(q.Z, 9))).Distinct().Count() == p.Ring.Count);
        check("S168 ② L형 H 3 — 저판 2.0 × 0.4 + 벽 (0.4 + 0.3)/2 × 2.6 + 버림 2.2 × 0.1 = 1.93 · 겹친 점 없음",
            Math.Abs(sL1.StructArea - (0.8 + 0.91 + 0.22)) < 1e-9 && noDup, $"{sL1.StructArea:F6}");
        //   모든 링이 반시계(넓이 +)
        bool ccw = true;
        foreach (var info in WallSection.Kinds)
        {
            var t = WallSection.DefaultTable(info.Id);
            foreach (var r in t.Rows) foreach (bool cut in new[] { true, false })
                ccw &= WallSection.BuildRow(info.Id, r, r[0], c, cut).Parts.All(p => WallSection.Area(p.Ring) > 0);
        }
        check("S168 ② 모든 형식 · 모든 줄 · 절토/성토 — 조각 링이 반시계(넓이 > 0)", ccw, "");

        // ③ 표 고르기 — H = h + Df 올림 · 첫 줄 아래 · 표 밖
        var r1 = WallSection.Lookup(tT, 3.6, out bool o1, out double h1);       // H 4.6 → 5.0 줄(T-5)
        var r2 = WallSection.Lookup(tT, 1.0, out bool o2, out double h2);       // H 2.0 → 첫 줄(3.0) · 표 밖 아님
        var r3 = WallSection.Lookup(tT, 7.5, out bool o3, out double h3);       // H 8.5 → 마지막 줄 + 표 밖
        check("S168 ③ 표 고르기 — 보이는 3.6 → H 4.6 → 5.0 줄 · 1.0 → 첫 줄 · 7.5 → 마지막 줄 + 표 밖",
            r1[0] == 5.0 && !o1 && Math.Abs(h1 - 4.6) < 1e-12 && r2[0] == 3.0 && !o2 && r3[0] == 8.0 && o3 && Math.Abs(h3 - 8.5) < 1e-12,
            $"{r1[0]}/{o1} · {r2[0]}/{o2} · {r3[0]}/{o3}");
        //   벽은 실제 높이로 선다(줄 H가 아니라) — 보이는 3.6 · Df 1 → 꼭대기 z = 3.6
        var sB = WallSection.Build(tT, 3.6, c, cut: true);
        double zTop = sB.Parts.SelectMany(p => p.Ring).Max(q => q.Z);
        check("S168 ③ 벽 꼭대기 = 보이는 높이(3.6) · 기초 · 두께는 고른 줄(5.0) 값", Math.Abs(zTop - 3.6) < 1e-12 && Math.Abs(sB.HTotal - 4.6) < 1e-12, $"{zTop}");

        // ④ 보강토 — L = max(0.7H, 2.5) · Df = max(0.6, H/20) · 터파기 뒤 끝 = 보강재 끝 · 바닥 = 패드 아래
        var tR = WallSection.DefaultTable(WallSection.Kind.보강토);
        var r6 = tR.Rows.First(r => Math.Abs(r[0] - 6.0) < 1e-9);
        var r14 = tR.Rows.First(r => Math.Abs(r[0] - 3.5) < 1e-9);
        var r12 = tR.Rows[^1];
        check("S168 ④ 보강토 규칙 — H 6: L 4.2 · Df 0.6 / H 3.5: L 2.5(최소) / H 12: L 8.4 · Df 0.6",
            Math.Abs(r6[1] - 4.2) < 1e-9 && Math.Abs(WallSection.Col(WallSection.Kind.보강토, r6, "Df") - 0.6) < 1e-9 && Math.Abs(r14[1] - 2.5) < 1e-9 && Math.Abs(r12[1] - 8.4) < 1e-9
            && Math.Abs(WallSection.Col(WallSection.Kind.보강토, r12, "Df") - 0.6) < 1e-9,
            $"{r6[1]} · {r14[1]} · {r12[1]}");
        var sR = WallSection.BuildRow(WallSection.Kind.보강토, r6, 6.0, c, cut: false);
        double x0 = 0.05 * -0.6;
        check("S168 ④ 보강토 터파기 — 뒤 끝 = 앞면 + L 4.2 · 앞 끝 = 패드 여유 · 바닥 = −Df − 패드 0.15 · 블록 · 패드만 구조물",
            Math.Abs(sR.XBack - (x0 + 4.2)) < 1e-9 && Math.Abs(sR.XFront - (x0 - 0.075)) < 1e-9 && Math.Abs(sR.ZBottom - (-0.75)) < 1e-9
            && sR.Parts.Where(p => p.Structure).Select(p => p.Material).OrderBy(m => m).SequenceEqual(new[] { "기초패드", "블록" }),
            $"앞 {sR.XFront:F3} · 뒤 {sR.XBack:F3} · 바닥 {sR.ZBottom:F3}");

        // ⑤ 터파기 범위 — 역T형 H 5: 앞 끝 = 앞굽 끝 − 버림 돌출 · 뒤 끝 = 뒷굽 끝 + 버림 돌출 · 바닥 = −Df − 버림 · 패널식은 도랑
        double xs0 = 0.02 * -0.5;
        check("S168 ⑤ 역T형 H 5 터파기 범위 — 앞 −0.61 · 뒤 2.79(앞면 기울기만큼 −0.01) · 바닥 −1.1",
            Math.Abs(sT5.XFront - (xs0 - 0.5 - 0.1)) < 1e-12 && Math.Abs(sT5.XBack - (xs0 + 0.5 + 2.2 + 0.1)) < 1e-12 && Math.Abs(sT5.ZBottom + 1.1) < 1e-12 && !sT5.Trench,
            $"앞 {sT5.XFront:F4} · 뒤 {sT5.XBack:F4} · 바닥 {sT5.ZBottom:F4}");
        var tP = WallSection.DefaultTable(WallSection.Kind.패널식);
        var sP = WallSection.Build(tP, 4.0, c, cut: true);
        // ★[2판 · 검토 높음 1] 패널 · 되메움은 깎기면(x = 0.01 z) 앞 · 도랑 뒤 끝도 깎기면 앞(벽 뒤 터파기 0)
        bool allFront = sP.Parts.All(p => p.Ring.All(q => q.X <= 0.01 * q.Z + 1e-9));
        check("S168 ⑤ 패널식 — 도랑(기초부만) · 폭 = 기초 0.6 + 버림 돌출 둘 · 뒤 끝 ≤ 깎기면 · 모든 조각이 깎기면 앞 · 패널 뒤 되메움은 구조물 아님",
            sP.Trench && Math.Abs(sP.XBack - sP.XFront - 0.8) < 1e-12 && sP.XBack <= 0.01 * sP.ZBottom + 1e-12 && allFront
            && sP.Parts.Any(p => p.Material == "패널 뒤 되메움" && !p.Structure), $"앞 {sP.XFront:F3} · 뒤 {sP.XBack:F3}");
        // ★[2판 · 검토 중간 6] 보강토 근입 — 앞 땅 3H:2V(0.6)면 H/5 → Df = 0.25h · h 6 → Df 1.5 · H 7.5 → L 5.3(0.7H = 5.25 올림)
        var sRs = WallSection.Build(tR, 6.0, c, cut: false, frontSlope: 0.6);
        double Ls = sRs.XBack - 0.05 * -1.5;
        check("S168 ⑤ 보강토 앞 땅 3H:2V — 근입 1.5(H/5) · H 7.5 · L 5.3", Math.Abs(sRs.Df - 1.5) < 1e-9 && Math.Abs(sRs.HTotal - 7.5) < 1e-9 && Math.Abs(Ls - 5.3) < 1e-9,
            $"Df {sRs.Df} · H {sRs.HTotal} · L {Ls:F3}");
        // ★[2판 · 검토 중간 7] 첫 줄보다 한 칸 넘게 낮은 벽은 Below
        var sLow = WallSection.Build(tT, 0.5, c, cut: true);
        var sMid = WallSection.Build(tT, 1.6, c, cut: true);
        check("S168 ⑤ 낮은 벽 — 역T 보이는 0.5(H 1.5 · 첫 줄 3.0)는 Below · 1.6(H 2.6)은 아님", sLow.Below && !sMid.Below, $"{sLow.Below}/{sMid.Below}");

        // ⑥ 표 기록 — 첫 표는 기록 없음 · 고친 표 왕복 · 틀린 기록은 첫 표 + 까닭
        check("S168 ⑥ 첫 표는 기록 없음(null)", WallSection.Encode(WallSection.DefaultTable(WallSection.Kind.역T형)) == null, "");
        var mod = tT.Clone(); mod.Rows[4][4] = 2.5;                              // H 5 뒷굽 2.2 → 2.5(토질부 값이라 치고)
        var enc = WallSection.Encode(mod);
        bool okR = WallSection.TryDecode(WallSection.Kind.역T형, enc, out var back, out string wr);
        check("S168 ⑥ 고친 표 왕복(H 5 뒷굽 2.5)", okR && back.Print == mod.Print && wr == "", wr);
        var bad = new List<(char, object)>(enc!); bad[1] = ('I', (int)WallSection.Kind.보강토);
        check("S168 ⑥ 다른 형식 기록은 첫 표 + 까닭", !WallSection.TryDecode(WallSection.Kind.역T형, bad, out var b2, out string w2) && b2.Print == tT.Print && w2.Contains("형식"), w2);
        var mod2 = tT.Clone(); mod2.Rows[3][0] = 6.0;                            // H 순서 깨짐
        check("S168 ⑥ H 순서가 깨진 표는 쓸 수 없다", !WallSection.Valid(mod2, out string w3) && w3.Contains("H"), w3);

        // ⑦ ★[v104 2판 재검토] 부벽(평균) · 보강토 수직간격 · 전면 기울기 칸 · 돌쌓기 배수층 · 기초 밑 버림 · 찰쌓기 뒤채움 콘크리트
        var tC = WallSection.DefaultTable(WallSection.Kind.부벽식);
        var rC = tC.Rows[0];                                                      // H 8.5 · 부벽 간격 2.8 · 두께 0.3
        var sC = WallSection.BuildRow(WallSection.Kind.부벽식, rC, 8.5, c, cut: true);
        var cf = sC.Parts.Single(p => p.Material == "부벽(평균)");
        double noCf = sC.Parts.Where(p => p.Fraction >= 1).Sum(p => WallSection.Area(p.Ring));
        check("S168 ⑦ 부벽식 — 간격 2.8(H/3) · 두께 0.3 · 부벽 세모 몫 0.3/2.8 · 구조물 넓이 = 온 조각 + 몫 × 세모",
            Math.Abs(WallSection.Col(WallSection.Kind.부벽식, rC, "s_cf") - 2.8) < 1e-9 && Math.Abs(cf.Fraction - 0.3 / 2.8) < 1e-12 && cf.Structure
            && Math.Abs(sC.StructArea - (noCf + cf.Fraction * WallSection.Area(cf.Ring))) < 1e-9, $"몫 {cf.Fraction:F4} · 세모 {WallSection.Area(cf.Ring):F3} · 합 {sC.StructArea:F3}");
        var rR5 = tR.Rows.First(r => Math.Abs(r[0] - 5.0) < 1e-9);
        var rR5b = (double[])rR5.Clone(); rR5b[System.Array.IndexOf(WallSection.Of(WallSection.Kind.보강토).Cols, "batter")] = 0.10;
        var sR5 = WallSection.BuildRow(WallSection.Kind.보강토, rR5, 5.0, c, cut: false);
        var sR5b = WallSection.BuildRow(WallSection.Kind.보강토, rR5b, 5.0, c, cut: false);
        double topX(WallSection.Shape sh) => sh.Parts.Single(p => p.Material == "블록").Ring.Where(q => q.Z > 4.3).Min(q => q.X);
        check("S168 ⑦ 보강토 — 수직간격 0.6 · 전면 기울기 칸이 블록 기울기를 정한다(1:0.05 → 1:0.10)",
            Math.Abs(WallSection.Col(WallSection.Kind.보강토, rR5, "Sv") - 0.6) < 1e-12 && Math.Abs(sR5.FaceBatter - 0.05) < 1e-12 && Math.Abs(sR5b.FaceBatter - 0.10) < 1e-12
            && Math.Abs(topX(sR5b) - topX(sR5) - 0.05 * 4.4) < 1e-9, $"{topX(sR5):F3} → {topX(sR5b):F3}");
        var tW = WallSection.DefaultTable(WallSection.Kind.찰쌓기);
        var sW = WallSection.BuildRow(WallSection.Kind.찰쌓기, tW.Rows[1], 3.0, c, cut: false);
        var tD = WallSection.DefaultTable(WallSection.Kind.메쌓기);
        var sD = WallSection.BuildRow(WallSection.Kind.메쌓기, tD.Rows[0], 1.5, c, cut: true);
        check("S168 ⑦ 찰쌓기 — 뒤채움 콘크리트는 구조물 · 배수층 0.3 · 기초 밑 버림 · 바닥 = −근입 − 버림 / 메쌓기 — 조약돌 뒤채움은 구조물 아님 · 배수층 없음",
            sW.Parts.Any(p => p.Material == "뒤채움 콘크리트" && p.Structure) && sW.Parts.Any(p => p.Material == "배수층" && !p.Structure) && sW.Parts.Any(p => p.Material == "버림")
            && Math.Abs(sW.ZBottom - (-0.6 - 0.1)) < 1e-12 && sD.Parts.Any(p => p.Material == "뒤채움(조약돌)" && !p.Structure) && !sD.Parts.Any(p => p.Material == "배수층"),
            $"찰 바닥 {sW.ZBottom:F2} · 앞 {sW.XFront:F3} · 뒤 {sW.XBack:F3}");
    }
}
