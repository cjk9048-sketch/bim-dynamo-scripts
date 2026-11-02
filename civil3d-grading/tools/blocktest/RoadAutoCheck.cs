// ── S185 ★★[v111.7.1 · v111.7.2] 진입도로 [업데이트] 무엇을 다시 만들까 · [PVI 넣기] 확대할 종단도 · 확대 창 · 종단경사 경고(20 %) ──
//   시계 · 입력 거르개(PreTranslateMessage) · 화면은 화면 없는 Civil에서 못 돈다 — 그 판정만 떼어 낸 Core 함수(RoadAutoRules · ProfileSheetRules)를 본다(만든 값만)
using DH.Grading.Core;

static class RoadAutoCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S185 진입도로 [업데이트](무엇을) · PVI 확대 · 종단경사 경고 ==");
        // ★[v111.7.2 · JACK 1102 «마지막에 팝업창 단추를 누르면 한꺼번에»] 저절로 보낼까 판정(① Decide)은 저절로 다시 만들기와 함께 없앰 — ② 무엇을 · ③ 확대만 남음

        // ② 무엇을 — 지표면(옛것 · 없음) · 종단도(옛것 + 자리 + 안 겹침) · 횡단도(그린 적 + 자리 + 그릴 수 있음 + (옛것 · 새 지표면))
        {
            RoadAutoRules.Plan C(bool sS = false, bool sM = false, bool shS = false, bool shP = true, bool shO = false, bool xH = true, bool xS = false, bool xP = true, bool xR = false)
                => RoadAutoRules.Choose(sS, sM, shS, shP, shO, xH, xS, xP, xR);
            string P(RoadAutoRules.Plan p) => $"{(p.Surface ? "S" : "-")}{(p.Sheet ? "P" : "-")}{(p.Xsec ? "X" : "-")}" + (p.WhyNot.Length > 0 ? ":" + p.WhyNot : "");
            var cases = new (string Name, RoadAutoRules.Plan Got, string Want)[]
            {
                ("PVI 옮김(지표면 · 종단도 옛것 · 횡단도 있음)", C(sS: true, shS: true), "SPX"),
                ("지표면 없음(처음 뒤 지움)", C(sM: true, xH: false), "S--"),
                ("종단도만 옛것", C(shS: true), "-P-"),
                ("횡단도만 옛것", C(xS: true), "--X"),
                ("종단도 자리 모름 · 나머지 없음", C(shS: true, shP: false), "---:" + RoadAutoRules.WhySheetPlace),
                ("종단도 겹침 · 나머지 없음", C(shS: true, shO: true), "---:" + RoadAutoRules.WhySheetOverlap),
                ("종단도 자리 모름 + 지표면 옛것 = 지표면 · 횡단도는 함(까닭은 남김)", C(sS: true, shS: true, shP: false), "S-X:" + RoadAutoRules.WhySheetPlace),
                ("횡단도 자리 모름", C(xS: true, xP: false), "---:" + RoadAutoRules.WhyXsecPlace),
                ("횡단도 못 그림", C(xS: true, xR: true), "---:" + RoadAutoRules.WhyXsecRefused),
                ("횡단도 그린 적 없음 = 지표면만", C(sS: true, xH: false), "S--"),
                ("할 것 없음", C(), "---:" + RoadAutoRules.WhyNothing),
            };
            var bad = cases.Where(c => P(c.Got) != c.Want).Select(c => $"{c.Name}: {P(c.Got)} ≠ {c.Want}").ToList();
            check("S185 ② 무엇을(지표면 → 종단도 → 횡단도 · 자리 모름 · 겹침 · 못 그림 · 그릴 것 없음)", bad.Count == 0, string.Join(" | ", bad));
            check("S185 ② 글(로그 · 명령창)", C(sS: true, shS: true).What == "지표면 · 종단도 · 횡단도" && C(shS: true).What == "종단도" && !C().Any && C(sM: true).Any, C(sS: true, shS: true).What);
        }

        // ③ [PVI 넣기] 확대할 종단도 — 화면 가운데가 든 장 · 없으면 가장 가까운 장 · 깨진 상자 건너뜀 · 없으면 −1 / 확대 창(여유 · 가로세로 비)
        {
            var boxes = new List<(double X0, double Y0, double X1, double Y1)> { (0, 0, 400, 150), (500, 0, 900, 150), (double.NaN, 0, 1, 1), (1000, 0, 900, 50) };
            int a = ProfileSheetRules.NearestBox(boxes, 600, 70), b = ProfileSheetRules.NearestBox(boxes, 460, 70), c = ProfileSheetRules.NearestBox(boxes, 440, 70),
                d = ProfileSheetRules.NearestBox(boxes, -5000, 0), e = ProfileSheetRules.NearestBox(new List<(double, double, double, double)>(), 0, 0), f = ProfileSheetRules.NearestBox(boxes, 450, 70);
            check("S185 ③ 확대할 종단도(든 장 · 가까운 장 · 같으면 앞 장 · 깨진 상자 건너뜀 · 없으면 −1)", a == 1 && b == 1 && c == 0 && d == 0 && e == -1 && f == 0, $"{a} {b} {c} {d} {e} {f}");
            var (cx, cy, w, h) = ProfileSheetRules.ZoomWindow(500, 0, 900, 150, 16.0 / 9.0, 0.08);   // 넓은 종단도 — 너비가 정함
            var (cx2, cy2, w2, h2) = ProfileSheetRules.ZoomWindow(0, 0, 100, 400, 16.0 / 9.0, 0.08);  // 높은 상자 — 높이가 정함
            bool zOk = Math.Abs(cx - 700) < 1e-9 && Math.Abs(cy - 75) < 1e-9 && Math.Abs(w - 400 * 1.16) < 1e-6 && Math.Abs(w / h - 16.0 / 9.0) < 1e-9
                && Math.Abs(h2 - 400 * 1.16) < 1e-6 && Math.Abs(w2 / h2 - 16.0 / 9.0) < 1e-9 && Math.Abs(cx2 - 50) < 1e-9 && Math.Abs(cy2 - 200) < 1e-9;
            check("S185 ③ 확대 창(가운데 · 둘레 8 % · 화면 가로세로 비 유지)", zOk, $"{cx:F1},{cy:F1} {w:F1}×{h:F1} · {cx2:F1},{cy2:F1} {w2:F1}×{h2:F1}");
        }

        // ④ ★[v111.7.2 · JACK 1102 «20 %까지는 문제없이 — 그 이상이면 경고»] 종단경사 경고 — 20 % 넘는 접선만(문턱 고정 · «최대 경사 %»와 따로)
        {
            var ok = ProfileSheetRules.Steep(new List<(double, double)> { (0, 100), (50, 110), (100, 100) }, ProfileSheetRules.WarnGrade);              // ±20 % 딱 = 경고 없음
            var one = ProfileSheetRules.Steep(new List<(double, double)> { (0, 100), (20, 104.1), (40, 104.1) }, ProfileSheetRules.WarnGrade);         // 20.5 %
            var big = ProfileSheetRules.Steep(new List<(double, double)> { (0, 100), (20, 95), (27, 104.26), (40, 104.26), (60, 99) }, ProfileSheetRules.WarnGrade);   // −25 % · +132.3 % · −26.3 %
            var mid = ProfileSheetRules.Steep(new List<(double, double)> { (0, 100), (100, 118) }, ProfileSheetRules.WarnGrade);                       // 18 % — «최대 경사 15 %»와 20 % 사이 = 없음
            var junk = ProfileSheetRules.Steep(new List<(double, double)> { (0, 100), (0, 200), (double.NaN, 1), (10, 100.5) }, ProfileSheetRules.WarnGrade);
            check("S185 ④ 경고 — ±20 % 딱 = 없음 · 20.5 % = 1 · 가장 가파른 접선(132.3 % · 측점 20~27) · 개수 3 · 18 % = 없음 · 같은 측점 · NaN 건너뜀",
                ok.Count == 0 && one.Count == 1 && Math.Abs(one.WorstPct - 20.5) < 1e-9 && big.Count == 3 && Math.Abs(big.WorstPct - 132.2857142857) < 1e-6
                && big.S0 == 20 && big.S1 == 27 && mid.Count == 0 && junk.Count == 0 && double.IsNaN(ok.S0),
                $"{ok.Count} · {one.Count} {one.WorstPct:F2} · {big.Count} {big.WorstPct:F1} {big.S0}~{big.S1} · {mid.Count} · {junk.Count}");
        }

        // ⑤ ★[v111.7.2 · JACK 1102 «PVI점을 찍었으면 그 위치에도 측점»] 진입도로 측점에 더할 PVI — 기점 · 종점 빼고 · 1 cm 안은 하나 · 차례 · 이름은 «+» 측점
        {
            var pv = new List<double> { 0, 20, 47.5, 47.505, 60.004, 33.25, double.NaN, 120.0 - 0.005, 120 };
            var a = ProfileSheetRules.PviStations(pv, 0, 120, 0.01);
            string names = string.Join(" ", a.Select(v => StationNaming.Fmt(v, 20)));
            var none = ProfileSheetRules.PviStations(new List<double> { 0, 120 }, 0, 120);
            var bad = ProfileSheetRules.PviStations(new List<double> { 10 }, 50, 20);
            check("S185 ⑤ PVI 측점 — 기점 · 종점(1 cm 안 포함) 빼고 · 47.5 · 47.505 = 하나 · NaN 건너뜀 · 차례 · 주측점 PVI(20 · 60.004)는 «No.n»으로 합쳐질 자리 · 처음 · 끝만 = 없음 · 거꾸로 범위 = 없음",
                a.Count == 4 && a[0] == 20 && a[1] == 33.25 && a[2] == 47.5 && Math.Abs(a[3] - 60.004) < 1e-12
                && names == "No.1 No.1+13.25 No.2+07.50 No.3" && none.Count == 0 && bad.Count == 0,
                $"{string.Join(", ", a)} → {names}");
        }
    }
}
