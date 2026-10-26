// ── S172 ★★★[v105 · 계획_v105.md 6 · JACK 1022 «건교부 2008이 있다면 무조건 최신으로» · «도면마다 하나 고름»] 2008 도로옹벽 표준도 조건별 표 · 활동방지벽 ──
//   조건별 표가 모두 쓸 수 있는가 · 대표 줄을 도면 그림과 손 대조 · 조건 맞추기 · 표 기록 판 2 왕복 · 판 1(v104) 옮김 · 키 링 · 중력식 사다리꼴 ·
//   자르는 선 위 키 자리 · 키 밑 높이 · 바닥을 키 밑까지 내리면 공제가 키를 다 뺀다
//   값은 공공 표준도(정부 자료)만 — 현장 값 없음(공개 저장소)
using DH.Grading.Core;

static class WallStandardCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S172 2008 도로옹벽 표준도 — 조건별 표 · 대표 줄 대조 · 표 기록 판 2 · 활동방지벽 ==");
        var c = new WallSection.Common();
        var K = WallSection.Kind.역T형; var Lk = WallSection.Kind.L형; var M = WallSection.Kind.반중력식; var G = WallSection.Kind.중력식;

        // ① 조건마다 표가 쓸 수 있는가 · 링이 반시계 · 줄 수
        int nCond = 0, nBad = 0; string firstBad = "";
        foreach (var k in new[] { K, Lk, M, G })
            foreach (int back in new[] { 0, 1, 2 })
                foreach (bool seis in new[] { true, false })
                    foreach (double fn in k == G ? WallSection.FrontNs : new[] { 0.02 })
                        foreach (int fill in back == 0 ? new[] { 0 } : WallSection.FillChoices(k, seis))
                        {
                            var cd = WallSection.Normalize(k, new WallSection.Cond(back, fill, seis, fn));
                            var t = WallSection.DefaultTable(k, cd);
                            nCond++;
                            bool ok = t.Rows.Count > 0 && WallSection.Valid(t, out _) && t.Cond == cd
                                      && t.Rows.All(r => WallSection.BuildRow(k, r, r[0], c, true).Parts.All(p => WallSection.Area(p.Ring) > 0));
                            if (!ok) { nBad++; if (firstBad.Length == 0) firstBad = $"{k} {cd}"; }
                        }
        check($"S172 ① 조건별 표준 표 {nCond}개 — 모두 쓸 수 있는 표 · 링 반시계", nBad == 0, firstBad);
        check("S172 ① 줄 수 — 역T 평평 11 · 역T 1:1.5 ∞ 미고려 11 · L형 1:1.8 ½H 고려 8(7 m 넘음은 표준도에 없음) · L형 1:1.5 ¼H 고려 11 · 반중력 5 · 중력 6",
            WallSection.DefaultTable(K).Rows.Count == 11 && WallSection.DefaultTable(K, new(2, 4, false)).Rows.Count == 11
            && WallSection.DefaultTable(Lk, new(1, 1, true)).Rows.Count == 8 && WallSection.DefaultTable(Lk, new(2, 0, true)).Rows.Count == 11
            && WallSection.DefaultTable(M, new(1, 3, true)).Rows.Count == 5 && WallSection.DefaultTable(G, new(2, 4, true, 0.2)).Rows.Count == 6,
            $"{WallSection.DefaultTable(Lk, new(1, 1, true)).Rows.Count}");

        // ② 대표 줄 — 2008 일반도 그림과 손 대조(T-160 · T-12 · L-60 · M-20 · M-29 · 중력 G-5)
        double[] Row(WallSection.Kind k, WallSection.Cond cd, double h) => WallSection.DefaultTable(k, cd).Rows.First(r => Math.Abs(r[0] - h) < 1e-9);
        bool Eq(double[] r, params double[] w) => r.Length == w.Length && r.Zip(w).All(z => Math.Abs(z.First - z.Second) < 1e-9);
        // T-160 H 7.5 · 1:1.5 · 2.0H · 미고려: t_top 0.3 · t_bot 0.9 · 앞굽 0.8 · 뒷굽 3.9 · 저판 0.9/0.7 · 키 3.6/0.5/0.75 · Df 1.0
        check("S172 ② T-160(7.5 m · 1:1.5 · 2.0H · 지진 미고려) = 그림", Eq(Row(K, new(2, 3, false), 7.5), 7.5, 0.3, 0.9, 0.8, 3.9, 0.9, 0.7, 3.6, 0.5, 0.75, 1.0),
            string.Join(",", Row(K, new(2, 3, false), 7.5)));
        // T-12 H 3.0 · 1:1.8 · ¼H · 고려: 0.3 · 0.4 · 0.3 · 1.5 · 0.4/0.4 · 키 1.35/0.4/0.4 · Df 1.0
        check("S172 ② T-12(3.0 m · 1:1.8 · ¼H · 지진 고려) = 그림", Eq(Row(K, new(1, 0, true), 3.0), 3.0, 0.3, 0.4, 0.3, 1.5, 0.4, 0.4, 1.35, 0.4, 0.4, 1.0),
            string.Join(",", Row(K, new(1, 0, true), 3.0)));
        // L-60 H 6.0 · 1:1.5 · ¼H · 고려: t_bot 0.6 · 뒷굽 3.6 · 저판 0.6/0.4 · 키 없음
        check("S172 ② L-60(6.0 m · 1:1.5 · ¼H) = 그림(앞굽 0 · 헌치 0.2 · 키 없음)", Eq(Row(Lk, new(2, 0, true), 6.0), 6.0, 0.3, 0.6, 0, 3.6, 0.6, 0.4, 0, 0, 0, 1.0),
            string.Join(",", Row(Lk, new(2, 0, true), 6.0)));
        // M-20 H 3.5 · 1:1.5 · 1.0H · 고려(= 2003 CAD): 0.35 · 0.7 · 1.35 · 0.7 · 키 1.55/0.4/0.4
        check("S172 ② M-20(3.5 m · 1:1.5 · 1.0H) = 2008 그림 = 2003 CAD", Eq(Row(M, new(2, 2, true), 3.5), 3.5, 0.3, 0.7, 0.35, 1.35, 0.7, 0.7, 1.55, 0.4, 0.4, 1.0),
            string.Join(",", Row(M, new(2, 2, true), 3.5)));
        check("S172 ② M-29(4.0 m · 1:1.8 · ∞ · 미고려) = 2008 그림", Eq(Row(M, new(1, 4, false), 4.0), 4.0, 0.3, 1.05, 0.4, 2.15, 0.85, 0.85, 2.3, 0.45, 0.45, 1.0),
            string.Join(",", Row(M, new(1, 4, false), 4.0)));
        // G-5(3.0 m) 앞면 1:0.10 · 1:1.8 · ∞: B 2.6 · B1(키 앞면) 1.6 / 평평 G-1 1:0.20 3.0 m: B 2.4
        check("S172 ② 중력식 3.0 m — 1:0.10 · 1:1.8 · ∞ = B 2.6 · 키 1.6(G-5 표) · 평평 1:0.20 = B 2.4(G-1)",
            Eq(Row(G, new(1, 4, true, 0.10), 3.0), 3.0, 0.4, 2.6, 0.10, 1.6, 0.3, 0.3, 1.0) && Eq(Row(G, new(0, 0, true, 0.20), 3.0), 3.0, 0.4, 2.4, 0.20, 0, 0, 0, 1.0),
            string.Join(",", Row(G, new(1, 4, true, 0.10), 3.0)));

        // ③ 조건 맞추기 — 반중력 ¼H → 1.0H(그 칸이 덮는다) · 고려 + ∞ → 2.0H(고려에 ∞ 없음) · 중력식은 지진 늘 참 · 평평이면 쌓인 높이 0 · 조건 없는 형식은 첫 값
        check("S172 ③ 조건 맞추기",
            WallSection.Normalize(M, new(1, 0, true)).Fill == 2 && WallSection.Normalize(K, new(2, 4, true)).Fill == 3
            && WallSection.Normalize(G, new(1, 2, false, 0.11)) == new WallSection.Cond(1, 2, true, 0.10) && WallSection.Normalize(K, new(0, 3, false)).Fill == 0
            && WallSection.Normalize(WallSection.Kind.보강토, new(2, 3, false)) == WallSection.Cond.Default, "");
        check("S172 ③ 조건 글 · 도면 범위 — «벽 뒤 1:1.5 비탈 · 쌓인 높이 2.0H · 지진 미고려» · T-… 중 n장",
            WallSection.CondText(K, new(2, 3, false)) == "벽 뒤 1:1.5 비탈 · 쌓인 높이 2.0H · 지진 미고려" && WallSection.StdDrawings(K, new(2, 3, false)).StartsWith("T-")
            && WallSection.StdDrawings(K, WallSection.Cond.Default) == "T-1 ~ T-11 중 11장", WallSection.StdDrawings(K, new(2, 3, false)));

        // ④ 표 기록 판 2 — 첫 조건 · 안 고침 = 기록 없음 · 조건만 = 7칸(읽으면 그 조건 표준 표) · 고친 표 왕복 · 판 1(v104) 옮김
        check("S172 ④ 첫 조건 · 표준 표 = 기록 없음", WallSection.Encode(WallSection.DefaultTable(K)) == null, "");
        var t15 = WallSection.DefaultTable(K, new(2, 3, false));
        var e15 = WallSection.Encode(t15);
        bool okC = e15 != null && e15.Count == 7 && WallSection.TryDecode(K, e15, out var d15, out string w15) && d15.Print == t15.Print && w15 == "";
        check("S172 ④ 조건만(안 고침) — 7칸 · 읽으면 그 조건 표준 표", okC, e15 == null ? "null" : $"{e15.Count}칸");
        var m15 = t15.Clone(); m15.Rows[2][4] += 0.1;
        var em = WallSection.Encode(m15);
        check("S172 ④ 조건 + 고친 표 왕복", em != null && WallSection.TryDecode(K, em, out var dm, out _) && dm.Print == m15.Print && WallSection.IsCustom(dm), "");
        // 판 1: [1 · 형식 · 칸 8 · 줄 1 · 값 8개] — v104 역T 칸(키 없음)
        var v1 = new List<(char, object)> { ('I', 1), ('I', (int)K), ('I', 8), ('I', 1) };
        foreach (double v in new[] { 4.0, 0.3, 0.45, 0.4, 2.0, 0.45, 0.45, 1.0 }) v1.Add(('D', v));
        bool okV1 = WallSection.TryDecode(K, v1, out var dv1, out string wv1);
        check("S172 ④ 판 1(v104) 기록 → 새 칸(키 0 · Df 끝) · 조건 첫 값 · 고친 표로 남음",
            okV1 && wv1 == "" && dv1.Rows.Count == 1 && Eq(dv1.Rows[0], 4.0, 0.3, 0.45, 0.4, 2.0, 0.45, 0.45, 0, 0, 0, 1.0) && dv1.Cond == WallSection.Cond.Default && WallSection.IsCustom(dv1),
            wv1);
        var g1 = new List<(char, object)> { ('I', 1), ('I', (int)G), ('I', 4), ('I', 1), ('D', 2.0), ('D', 0.4), ('D', 1.7), ('D', 1.0) };
        check("S172 ④ 판 1 중력식(4칸) → 앞면 1:0.02 · 키 0", WallSection.TryDecode(G, g1, out var dg1, out _) && Eq(dg1.Rows[0], 2.0, 0.4, 1.7, 0.02, 0, 0, 0, 1.0), "");

        // ⑤ 활동방지벽 링 — T-160: 저판 앞 끝에서 3.6 · 폭 0.5 · 깊이 0.75(저판 밑) · 중력식 사다리꼴(뒷면 60°)
        var r160 = Row(K, new(2, 3, false), 7.5);
        var s160 = WallSection.BuildRow(K, r160, 7.5, c, cut: true);
        var key = s160.Parts.Single(p => p.Material == "활동방지벽");
        double xToe = 0.02 * (-1.0 + 0.9) - 0.8;
        check("S172 ⑤ 역T 키 — 넓이 0.375 · 앞면 = 저판 앞 끝 + 3.6 · 밑 = 저판 밑 − 0.75 · 구조물 · 터파기 바닥은 그대로(버림 밑)",
            Math.Abs(WallSection.Area(key.Ring) - 0.375) < 1e-9 && Math.Abs(key.Ring.Min(q => q.X) - (xToe + 3.6)) < 1e-9 && Math.Abs(key.Ring.Min(q => q.Z) - (-1.0 - 0.75)) < 1e-9
            && key.Structure && Math.Abs(s160.ZBottom - (-1.1)) < 1e-9, $"{WallSection.Area(key.Ring):F4} · x {key.Ring.Min(q => q.X):F3}");
        var sg = WallSection.BuildRow(G, Row(G, new(1, 4, true, 0.10), 3.0), 3.0, c, cut: true);
        var gk = sg.Parts.Single(p => p.Material == "활동방지벽");
        double gArea = (0.3 + 0.3 + 0.3 / Math.Tan(Math.PI / 3)) / 2 * 0.3;
        var gc = sg.Parts.Single(p => p.Material == "콘크리트");
        check("S172 ⑤ 중력식 키 사다리꼴 넓이 · 앞면 경사 1:0.10이 몸체 앞면을 정한다",
            Math.Abs(WallSection.Area(gk.Ring) - gArea) < 1e-9 && Math.Abs(sg.FaceBatter - 0.10) < 1e-12 && Math.Abs(gc.Ring.Min(q => q.X) - 0.10 * -1.0) < 1e-9,
            $"{WallSection.Area(gk.Ring):F4} / {gArea:F4}");
        var badG = WallSection.DefaultTable(G, new(1, 4, true, 0.10)).Clone(); badG.Rows[0][4] = badG.Rows[0][2] - 0.35;   // 위 폭(0.47)이 밑폭 밖
        check("S172 ⑤ 중력식 키 위 폭(뒷면 60°)이 밑폭 밖이면 쓸 수 없는 표", !WallSection.Valid(badG, out string wg) && wg.Contains("활동방지벽"), wg);

        // ⑥ 자르는 선 위 키 자리 · 키 밑 높이 · 바닥을 키 밑까지 내리면 공제가 키를 다 뺀다(계획 검토 높음 1)
        var tab = WallSection.DefaultTable(K, new(2, 3, false));
        int ri = tab.Rows.FindIndex(r => Math.Abs(r[0] - 7.5) < 1e-9);
        var rec = new WallExcav.SecRecord { Margin = 0.5, Common = c };
        rec.Tables[K] = tab;
        var ln = new WallExcav.RecLine { Kind = K, Soil = 1, Up = true, SlopeW = 0.01 };
        for (int i = 0; i <= 10; i++) ln.Pts.Add(new WallExcav.RecPt(i * 2.0, 0, 0, 7.5, 1.0, ri));   // 앞면 선 = x축 · 흙 = 왼쪽(+y)
        rec.Lines.Add(ln);
        // 자르는 선: (10, −3) → (10, 9) — 앞면 선과 직각 · 흙 쪽으로
        var spans = WallExcav.KeySpans(rec, 10, -3, 10, 9);
        double kx0 = 3 + key.Ring.Min(q => q.X), kx1 = 3 + key.Ring.Max(q => q.X);
        check("S172 ⑥ 키 자리 — 한 구간 · 자르는 선 시작에서 3 + 키 x(직각이라 늘임 없음)", spans.Count == 1 && Math.Abs(spans[0].Lo - kx0) < 1e-9 && Math.Abs(spans[0].Hi - kx1) < 1e-9,
            spans.Count == 0 ? "없음" : $"{spans[0].Lo:F3}~{spans[0].Hi:F3} / {kx0:F3}~{kx1:F3}");
        double kb = WallExcav.KeyBottomAt(rec, 10, (key.Ring.Min(q => q.X) + key.Ring.Max(q => q.X)) / 2);
        check("S172 ⑥ 키 밑 높이 = 앞 땅 0 − Df 1.0 − 키 0.75", Math.Abs(kb - (-1.75)) < 1e-9 && double.IsNaN(WallExcav.KeyBottomAt(rec, 10, 1.0)), $"{kb}");
        // 바닥 배열: 버림 밑(−1.1) · 키 자리만 −1.75(앞뒤 0.1 mm 점) · 지표 = 벽 위(6.5) — 공제 차 = 키가 버림 밑으로 나온 몫 0.5 × 0.65
        var xsL = new List<double>(); for (double x = -3; x <= 9 + 1e-9; x += 0.25) xsL.Add(x);
        foreach (var e in new[] { kx0 - 1e-4, kx0 + 1e-4, kx1 - 1e-4, kx1 + 1e-4 }) xsL.Add(e - 3);
        xsL.Sort();
        // Integrate의 xs는 −wl ~ wr 오프셋 — 자르는 선 길이 12 · wl = 3 · wr = 9
        var xsA = xsL.Select(x => x).ToArray();
        double[] lo0 = xsA.Select(x => -1.1).ToArray();
        double[] lo1 = xsA.Select(x => x + 3 > kx0 && x + 3 < kx1 ? -1.75 : -1.1).ToArray();
        double[] hi = xsA.Select(x => 6.5).ToArray();
        var s0 = WallExcav.Integrate(rec, 10, -3, 10, 9, 3, 9, xsA, lo0, hi);
        var s1 = WallExcav.Integrate(rec, 10, -3, 10, 9, 3, 9, xsA, lo1, hi);
        check("S172 ⑥ 바닥을 키 밑까지 내리면 공제가 키의 버림 밑 몫(0.5 × 0.65 = 0.325)만큼 늘어난다(터파기도 같은 몫 늘어 되메우기는 그대로)",
            Math.Abs(s1.Struct - s0.Struct - 0.325) < 0.01, $"{s1.Struct - s0.Struct:F4}");
    }
}
